"""
NARS Segmentation Service
Stateless inference microservice: satellite/aerial tile in -> GeoJSON draft
features (roads as LineStrings, buildings as Polygons) out.

This service owns no data. nars-api (.NET) is responsible for persisting
results into ai_draft_features and for auth/business rules. This service
is only reachable from inside the cluster network.
"""

import logging
import os
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, HTTPException, UploadFile

from app.model import SegmentationModel, TileTooLargeError
from app.postprocess import mask_to_linestrings, mask_to_polygons
from app.schemas import FeatureCollection, SegmentResponse

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("nars-roads")

INTERNAL_TOKEN = os.environ.get("NARS_ROADS_INTERNAL_TOKEN")
WEIGHTS_PATH = os.environ.get(
    "NARS_ROADS_WEIGHTS_PATH", "weights/unet_r34_multiclass.pth"
)
TILE_SIZE = int(os.environ.get("NARS_ROADS_TILE_SIZE", "1024"))
# Upper bound on a single tile upload. Inference reads the whole tile into
# memory, so an oversized upload is a pod-level memory-exhaustion risk.
MAX_TILE_BYTES = int(os.environ.get("NARS_ROADS_MAX_TILE_BYTES", str(50 * 1024 * 1024)))

# Built lazily in lifespan so importing this module (tests, tooling, --reload)
# never forces a multi-hundred-MB weight load.
_model: SegmentationModel | None = None


@asynccontextmanager
async def lifespan(_app: FastAPI):
    global _model
    _model = SegmentationModel(weights_path=WEIGHTS_PATH, tile_size=TILE_SIZE)
    yield
    _model = None


app = FastAPI(
    title="NARS Segmentation Service",
    version="0.1.0",
    lifespan=lifespan,
)


def verify_internal_token(x_internal_token: str = Header(default=None)) -> None:
    """Shared-secret check. This service is only exposed inside the cluster
    network, but we still gate it so a compromised pod elsewhere in the mesh
    can't call it for free. Fails closed: if the token is not configured, all
    requests are rejected rather than silently allowing unauthenticated use."""
    if not INTERNAL_TOKEN or x_internal_token != INTERNAL_TOKEN:
        raise HTTPException(status_code=401, detail="Invalid internal token")


@app.get("/health")
def health() -> dict:
    """Liveness: the process is up. Does not require the model to be loaded."""
    return {"status": "ok", "model_loaded": _model.is_loaded if _model else False}


@app.get("/ready")
def ready() -> dict:
    """Readiness: only report ready when real weights are loaded, so the pod
    never receives traffic while serving random predictions."""
    if _model is None or not _model.is_loaded:
        raise HTTPException(status_code=503, detail="Model weights not loaded")
    return {"status": "ready", "model_loaded": True}


def _validate_threshold(threshold: float) -> float:
    if not 0.0 <= threshold <= 1.0:
        raise HTTPException(
            status_code=422, detail="threshold must be between 0.0 and 1.0"
        )
    return threshold


@app.post(
    "/segment",
    response_model=SegmentResponse,
    dependencies=[Depends(verify_internal_token)],
)
def segment(
    tile: UploadFile,
    min_lon: float,
    min_lat: float,
    max_lon: float,
    max_lat: float,
    threshold: float = 0.5,
) -> SegmentResponse:
    """
    Run inference on a single georeferenced tile.

    bbox is passed as four separate query params (min_lon, min_lat, max_lon,
    max_lat) rather than one packed string, so nars-api doesn't have to do
    any custom parsing on either side.

    Declared as a plain `def` (not `async def`) so FastAPI runs it in the
    threadpool: inference is CPU-bound and must not block the event loop,
    which would stall /health during long requests.
    """
    _validate_threshold(threshold)

    if tile.content_type not in ("image/tiff", "image/png", "image/jpeg"):
        raise HTTPException(
            status_code=415, detail=f"Unsupported content type: {tile.content_type}"
        )

    # Authoritative size cap: read one byte past the limit and reject if the
    # upload is bigger, so we never buffer an unbounded tile into memory.
    raw = tile.file.read(MAX_TILE_BYTES + 1)
    if len(raw) > MAX_TILE_BYTES:
        raise HTTPException(
            status_code=413,
            detail=f"Tile exceeds the {MAX_TILE_BYTES} byte limit",
        )
    if not raw:
        raise HTTPException(status_code=400, detail="Empty file upload")

    if _model is None:
        raise HTTPException(status_code=503, detail="Model not ready")

    try:
        road_prob, building_prob, transform = _model.predict(
            raw, bbox=(min_lon, min_lat, max_lon, max_lat)
        )
    except TileTooLargeError:
        raise HTTPException(
            status_code=413,
            detail="Tile decodes to more pixels than the service accepts",
        )
    except Exception:
        logger.exception("Inference failed")
        raise HTTPException(status_code=500, detail="Inference failed")

    roads = mask_to_linestrings(road_prob, transform, threshold=threshold)
    buildings = mask_to_polygons(building_prob, transform, threshold=threshold)

    return SegmentResponse(
        roads=FeatureCollection(features=roads),
        buildings=FeatureCollection(features=buildings),
    )
