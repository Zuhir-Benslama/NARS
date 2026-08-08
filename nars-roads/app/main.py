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

from fastapi import Depends, FastAPI, Header, HTTPException, UploadFile
from fastapi.responses import JSONResponse

from app.model import SegmentationModel
from app.postprocess import mask_to_linestrings, mask_to_polygons
from app.schemas import SegmentResponse

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("nars-roads")

INTERNAL_TOKEN = os.environ.get("NARS_ROADS_INTERNAL_TOKEN")
WEIGHTS_PATH = os.environ.get(
    "NARS_ROADS_WEIGHTS_PATH", "weights/unet_r34_multiclass.pth"
)
TILE_SIZE = int(os.environ.get("NARS_ROADS_TILE_SIZE", "1024"))

app = FastAPI(title="NARS Segmentation Service", version="0.1.0")
model = SegmentationModel(weights_path=WEIGHTS_PATH, tile_size=TILE_SIZE)


def verify_internal_token(x_internal_token: str = Header(default=None)) -> None:
    """Simple shared-secret check. This service is only exposed inside the
    cluster network, but we still gate it so a compromised pod elsewhere
    in the mesh can't call it for free."""
    if INTERNAL_TOKEN and x_internal_token != INTERNAL_TOKEN:
        raise HTTPException(status_code=401, detail="Invalid internal token")


@app.get("/health")
def health():
    return {"status": "ok", "model_loaded": model.is_loaded}


@app.post(
    "/segment",
    response_model=SegmentResponse,
    dependencies=[Depends(verify_internal_token)],
)
async def segment(
    tile: UploadFile,
    min_lon: float,
    min_lat: float,
    max_lon: float,
    max_lat: float,
    threshold: float = 0.5,
):
    """
    Run inference on a single georeferenced tile.

    bbox is passed as four separate query params (min_lon, min_lat, max_lon,
    max_lat) rather than one packed string, so nars-api doesn't have to do
    any custom parsing on either side.
    """
    if tile.content_type not in ("image/tiff", "image/png", "image/jpeg"):
        raise HTTPException(
            status_code=415, detail=f"Unsupported content type: {tile.content_type}"
        )

    raw = await tile.read()
    if not raw:
        raise HTTPException(status_code=400, detail="Empty file upload")

    try:
        road_prob, building_prob, transform = model.predict(
            raw, bbox=(min_lon, min_lat, max_lon, max_lat)
        )
    except Exception:
        logger.exception("Inference failed")
        raise HTTPException(status_code=500, detail="Inference failed")

    roads = mask_to_linestrings(road_prob, transform, threshold=threshold)
    buildings = mask_to_polygons(building_prob, transform, threshold=threshold)

    return JSONResponse(
        {
            "roads": {"type": "FeatureCollection", "features": roads},
            "buildings": {"type": "FeatureCollection", "features": buildings},
        }
    )
