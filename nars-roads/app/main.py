"""
NARS Segmentation Service
Stateless inference microservice: satellite/aerial tile in -> GeoJSON draft
features out. One model instance per feature type (a model registry) rather
than a shared multiclass network, so each checkpoint can be swapped and
released independently. Today only `buildings` has a checkpoint; `roads` is
documented in the registry so adding a road model is a config entry plus a
/segment/roads endpoint.

This service owns no data. nars-api (.NET) is responsible for persisting
results into ai_draft_features and for auth/business rules. This service
is only reachable from inside the cluster network.
"""

import logging
import os
import secrets
import threading
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, HTTPException, UploadFile

from app.model import (
    InvalidTileError,
    SegmentationModel,
    TileTooLargeError,
)
from app.postprocess import mask_to_polygons
from app.schemas import FeatureCollection, SegmentResponse

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("nars-roads")

INTERNAL_TOKEN = os.environ.get("NARS_ROADS_INTERNAL_TOKEN")
TILE_SIZE = int(os.environ.get("NARS_ROADS_TILE_SIZE", "1024"))
# Upper bound on a single tile upload. Inference reads the whole tile into
# memory, so an oversized upload is a pod-level memory-exhaustion risk.
MAX_TILE_BYTES = int(os.environ.get("NARS_ROADS_MAX_TILE_BYTES", str(50 * 1024 * 1024)))
# How many inferences may run concurrently before requests queue in the
# threadpool. Sized for the pod memory limit; raise/lower per deployment.
MAX_CONCURRENT_INFERENCES = max(
    1, int(os.environ.get("NARS_ROADS_MAX_CONCURRENT_INFERENCES", "2"))
)

# Model registry: feature type -> how to build its model. Each entry is an
# independent binary (foreground/background) checkpoint, so one task can be
# updated or rolled back without touching the others. Roads stays commented
# out until a road checkpoint exists; enabling it is this entry plus a
# /segment/roads endpoint (postprocess: mask_to_linestrings).
MODEL_SPECS: dict[str, dict] = {
    "buildings": {
        "weights_path": os.environ.get(
            "NARS_ROADS_WEIGHTS_PATH", "weights/unet_bldg_base.pth"
        ),
        "num_classes": 2,
    },
    # "roads": {
    #     "weights_path": os.environ.get("NARS_ROADS_ROADS_WEIGHTS_PATH"),
    #     "num_classes": 2,
    # },
}

# Built lazily in lifespan so importing this module (tests, tooling, --reload)
# never forces a multi-hundred-MB weight load.
_models: dict[str, SegmentationModel] = {}

# Each inference can peak at ~500MB (25M-px prob maps + windows), so unbounded
# threadpool concurrency on a 4Gi pod is an OOM risk. A small semaphore caps
# how many inferences run in parallel; the rest queue in the threadpool.
INFERENCE_SEMAPHORE = threading.BoundedSemaphore(MAX_CONCURRENT_INFERENCES)


@asynccontextmanager
async def lifespan(_app: FastAPI):
    global _models
    for task, spec in MODEL_SPECS.items():
        _models[task] = SegmentationModel(
            weights_path=spec["weights_path"],
            num_classes=spec["num_classes"],
            tile_size=TILE_SIZE,
        )
    yield
    _models = {}


app = FastAPI(
    title="NARS Segmentation Service",
    version="0.2.0",
    lifespan=lifespan,
)


def _buildings_loaded() -> bool:
    """Readiness signal for the only task with real weights today. A missing
    or unloaded model fails closed (never serves random predictions)."""
    model = _models.get("buildings")
    return model is not None and model.is_loaded


def verify_internal_token(
    x_internal_token: str | None = Header(default=None),
) -> None:
    """Shared-secret check. This service is only exposed inside the cluster
    network, but we still gate it so a compromised pod elsewhere in the mesh
    can't call it for free. Fails closed: if the token is not configured, all
    requests are rejected rather than silently allowing unauthenticated use."""
    if not INTERNAL_TOKEN or not secrets.compare_digest(
        x_internal_token or "", INTERNAL_TOKEN
    ):
        raise HTTPException(status_code=401, detail="Invalid internal token")


@app.get("/health")
def health() -> dict:
    """Liveness: the process is up. Does not require the model to be loaded."""
    return {"status": "ok", "model_loaded": _buildings_loaded()}


@app.get("/ready")
def ready() -> dict:
    """Readiness: only report ready when real weights are loaded, so the pod
    never receives traffic while serving random predictions."""
    if not _buildings_loaded():
        raise HTTPException(status_code=503, detail="Model weights not loaded")
    return {"status": "ready", "model_loaded": True}


def _validate_threshold(threshold: float) -> float:
    if not 0.0 <= threshold <= 1.0:
        raise HTTPException(
            status_code=422, detail="threshold must be between 0.0 and 1.0"
        )
    return threshold


def _validate_bbox(
    min_lon: float, min_lat: float, max_lon: float, max_lat: float
) -> None:
    """Reject inverted and out-of-range boxes before they reach from_bounds.

    An unvalidated box (e.g. min_lon > max_lon, lat > 90) would produce an
    inverted transform in model.predict and silently garbage GeoJSON."""
    if not (-180.0 <= min_lon <= 180.0) or not (-180.0 <= max_lon <= 180.0):
        raise HTTPException(status_code=422, detail="longitude must be in [-180, 180]")
    if not (-90.0 <= min_lat <= 90.0) or not (-90.0 <= max_lat <= 90.0):
        raise HTTPException(status_code=422, detail="latitude must be in [-90, 90]")
    if min_lon >= max_lon or min_lat >= max_lat:
        raise HTTPException(
            status_code=422,
            detail="bbox must have min_lon < max_lon and min_lat < max_lat",
        )


def _segment_task(
    task: str,
    tile: UploadFile,
    min_lon: float,
    min_lat: float,
    max_lon: float,
    max_lat: float,
    threshold: float,
) -> FeatureCollection:
    """Run inference for one registered task and convert the foreground mask
    to vector features. Shared by every /segment/<task> endpoint."""
    _validate_threshold(threshold)
    _validate_bbox(min_lon, min_lat, max_lon, max_lat)

    if tile.content_type not in ("image/tiff", "image/png", "image/jpeg"):
        raise HTTPException(
            status_code=415, detail=f"Unsupported content type: {tile.content_type}"
        )

    # Cheap readiness gate before buffering the upload: an unready task should
    # reject with 503 without reading (and discarding) up to MAX_TILE_BYTES.
    model = _models.get(task)
    if model is None or not model.is_loaded:
        raise HTTPException(status_code=503, detail=f"{task} model not ready")

    # Authoritative size cap: read up to the limit and reject if the upload
    # is bigger, so we never buffer an unbounded tile into memory.
    raw = tile.file.read(MAX_TILE_BYTES)
    if not raw:
        raise HTTPException(status_code=400, detail="Empty file upload")
    if len(raw) >= MAX_TILE_BYTES:
        # The upload may be exactly at the limit or larger. Check one more
        # byte to distinguish the two cases.
        extra = tile.file.read(1)
        if extra:
            raise HTTPException(
                status_code=413,
                detail=f"Tile exceeds the {MAX_TILE_BYTES} byte limit",
            )

    try:
        # Cap concurrent inferences so a burst of large tiles can't OOM the
        # 4Gi pod; extra requests queue here instead of stacking ~500MB each.
        # Postprocessing runs under the same permit: the prob maps (~300MB)
        # stay alive while find_contours allocates on top of them, so bounding
        # the whole pipeline keeps peak memory tight.
        acquired = INFERENCE_SEMAPHORE.acquire(timeout=30)
        if not acquired:
            raise HTTPException(  # noqa: TRY301 - re-raised by except HTTPException below
                status_code=503,
                detail="Server is at capacity; retry after a short delay",
            )
        try:
            fg_prob, transform = model.predict(
                raw, bbox=(min_lon, min_lat, max_lon, max_lat)
            )
            # Only the buildings postprocessor exists today. A future task
            # (e.g. roads) would dispatch to mask_to_linestrings here, so the
            # branch is dropped rather than left unreachable.
            features = mask_to_polygons(fg_prob, transform, threshold=threshold)
        finally:
            INFERENCE_SEMAPHORE.release()
    except TileTooLargeError as exc:
        raise HTTPException(
            status_code=413,
            detail="Tile decodes to more pixels than the service accepts",
        ) from exc
    except InvalidTileError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except HTTPException:
        raise
    except Exception as exc:
        logger.exception("Inference failed")
        raise HTTPException(status_code=500, detail="Inference failed") from exc

    return FeatureCollection(features=features)


@app.post(
    "/segment/buildings",
    response_model=SegmentResponse,
    dependencies=[Depends(verify_internal_token)],
)
def segment_buildings(
    tile: UploadFile,
    min_lon: float,
    min_lat: float,
    max_lon: float,
    max_lat: float,
    threshold: float = 0.5,
) -> SegmentResponse:
    """
    Run building inference on a single georeferenced tile.

    bbox is passed as four separate query params (min_lon, min_lat, max_lon,
    max_lat) rather than one packed string, so nars-api doesn't have to do
    any custom parsing on either side.

    Declared as a plain `def` (not `async def`) so FastAPI runs it in the
    threadpool: inference is CPU-bound and must not block the event loop,
    which would stall /health during long requests.
    """
    return SegmentResponse(
        buildings=_segment_task(
            "buildings",
            tile,
            min_lon,
            min_lat,
            max_lon,
            max_lat,
            threshold,
        )
    )
