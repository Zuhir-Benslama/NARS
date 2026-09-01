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

import concurrent.futures
import logging
import os
import secrets
import threading
from contextlib import asynccontextmanager
from typing import Annotated, Any, TypedDict

from fastapi import Depends, FastAPI, Header, HTTPException, Query, UploadFile

from app.config import env_int
from app.model import (
    InvalidTileError,
    SegmentationModel,
    TileTooLargeError,
)
from app.postprocess import mask_to_polygons
from app.schemas import FeatureCollection, SegmentResponse

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("nars-roads")


# Shared-secret for cluster-internal requests. Read once at import time: a
# runtime rotation of the env var (e.g. via a pod restart with a new mounted
# secret) is applied on the next process start, not on the fly. This is the
# intended model for a static, cluster-internal service.
INTERNAL_TOKEN = os.environ.get("NARS_ROADS_INTERNAL_TOKEN")
TILE_SIZE = env_int("NARS_ROADS_TILE_SIZE", 1024, minimum=16, maximum=8192)
# Upper bound on a single tile upload. Inference reads the whole tile into
# memory, so an oversized upload is a pod-level memory-exhaustion risk.
MAX_TILE_BYTES = env_int(
    "NARS_ROADS_MAX_TILE_BYTES",
    50 * 1024 * 1024,
    minimum=1024,
    maximum=1024 * 1024 * 1024,
)
# How many inferences may run concurrently before requests queue in the
# threadpool. Sized for the pod memory limit; raise/lower per deployment.
MAX_CONCURRENT_INFERENCES = env_int(
    "NARS_ROADS_MAX_CONCURRENT_INFERENCES", 2, minimum=1, maximum=64
)
# Wall-clock ceiling on a single predict() call.  A pathological tile that
# hangs the model would otherwise hold a semaphore slot indefinitely; with
# MAX_CONCURRENT_INFERENCES=2 just two such tiles exhaust all capacity.
INFERENCE_TIMEOUT = env_int(
    "NARS_ROADS_INFERENCE_TIMEOUT", 120, minimum=1, maximum=3600
)


# Model registry: feature type -> how to build its model. Each entry is an
# independent binary (foreground/background) checkpoint, so one task can be
# updated or rolled back without touching the others. Roads is not yet
# registered — no road checkpoint exists yet; adding it is a "roads" entry here
# plus a /segment/roads endpoint (postprocess: mask_to_linestrings).
class ModelSpec(TypedDict):
    """How to construct one task's SegmentationModel."""

    weights_path: str
    num_classes: int


MODEL_SPECS: dict[str, ModelSpec] = {
    "buildings": {
        "weights_path": os.environ.get(
            "NARS_ROADS_WEIGHTS_PATH", "weights/unet_bldg_base.pth"
        ),
        "num_classes": 2,
    },
}

# Built lazily in lifespan so importing this module (tests, tooling, --reload)
# never forces a multi-hundred-MB weight load.
_models: dict[str, SegmentationModel] = {}

# Each inference can peak at ~500MB (25M-px prob maps + windows), so unbounded
# threadpool concurrency on a 4Gi pod is an OOM risk. A small semaphore caps
# how many inferences run in parallel; the rest queue in the threadpool.
INFERENCE_SEMAPHORE = threading.BoundedSemaphore(MAX_CONCURRENT_INFERENCES)

# Dedicated executor for wrapping predict() with a wall-clock timeout.
# max_workers matches the semaphore so at most MAX_CONCURRENT_INFERENCES
# timeout-watchdog threads exist at any time.
_TIMEOUT_POOL = concurrent.futures.ThreadPoolExecutor(
    max_workers=MAX_CONCURRENT_INFERENCES,
    thread_name_prefix="infer-timeout",
)


def _load_model(task: str, spec: ModelSpec) -> SegmentationModel | None:
    """Construct one task's model, tolerating per-task failures.

    A corrupt or mismatched checkpoint must not take the whole service down:
    with several tasks registered, one bad model (e.g. a road checkpoint that
    fails to load) would otherwise crash startup for every task. A failed load
    is logged and that task is left unregistered — `/segment/<task>` and
    `/ready` then fail closed for it alone, while the other tasks keep serving.
    """
    try:
        return SegmentationModel(
            weights_path=spec["weights_path"],
            num_classes=spec["num_classes"],
            tile_size=TILE_SIZE,
        )
    except Exception:  # a load failure must never abort startup
        logger.exception(
            "Failed to load weights for task '%s'; it will be unavailable", task
        )
        return None


@asynccontextmanager
async def lifespan(_app: FastAPI):
    global _models
    for task, spec in MODEL_SPECS.items():
        model = _load_model(task, spec)
        if model is not None:
            _models[task] = model
    yield
    _models = {}


app = FastAPI(
    title="NARS Segmentation Service",
    version="0.2.0",
    lifespan=lifespan,
)


def _any_task_loaded() -> bool:
    """Readiness signal: at least one registered task has real weights loaded.
    A task whose weights are missing or failed to load (see `_load_model`)
    reports unready and fails closed (never serves random predictions), but a
    service with any healthy task can still serve. Returns False only when no
    registered task is usable — the pod must not receive traffic then."""
    return any(model is not None and model.is_loaded for model in _models.values())


def verify_internal_token(
    x_internal_token: str | None = Header(default=None),
) -> None:
    """Shared-secret check. This service is only exposed inside the cluster
    network, but we still gate it so a compromised pod elsewhere in the mesh
    can't call it for free. Fails closed: if the token is not configured, all
    requests are rejected rather than silently allowing unauthenticated use."""
    # Compare bytes, not str: h11 decodes header values as latin-1, so a
    # single non-ASCII byte in X-Internal-Token reaches us as e.g. "é" —
    # and secrets.compare_digest(str, str) raises TypeError on non-ASCII,
    # which would turn a bad token into an unhandled 500 instead of a 401.
    if not INTERNAL_TOKEN or not secrets.compare_digest(
        (x_internal_token or "").encode("utf-8"), INTERNAL_TOKEN.encode("utf-8")
    ):
        logger.warning("Rejected request with invalid internal token")
        raise HTTPException(status_code=401, detail="Invalid internal token")


@app.get("/health")
def health() -> dict:
    """Liveness: the process is up. Does not require the model to be loaded."""
    return {"status": "ok", "model_loaded": _any_task_loaded()}


@app.get("/ready")
def ready() -> dict:
    """Readiness: only report ready when real weights are loaded, so the pod
    never receives traffic while serving random predictions."""
    if not _any_task_loaded():
        raise HTTPException(status_code=503, detail="No model weights loaded")
    return {"status": "ready", "model_loaded": True}


def _validate_bbox(
    min_lon: float, min_lat: float, max_lon: float, max_lat: float
) -> None:
    """Reject inverted and out-of-range boxes before they reach from_bounds.

    An unvalidated box (e.g. min_lon > max_lon, lat > 90) would produce an
    inverted transform in model.predict and silently garbage GeoJSON."""
    if not (-180.0 <= min_lon <= 180.0):
        raise HTTPException(status_code=422, detail="min_lon must be in [-180, 180]")
    if not (-180.0 <= max_lon <= 180.0):
        raise HTTPException(status_code=422, detail="max_lon must be in [-180, 180]")
    if not (-90.0 <= min_lat <= 90.0):
        raise HTTPException(status_code=422, detail="min_lat must be in [-90, 90]")
    if not (-90.0 <= max_lat <= 90.0):
        raise HTTPException(status_code=422, detail="max_lat must be in [-90, 90]")
    if min_lon >= max_lon:
        raise HTTPException(
            status_code=422,
            detail="min_lon must be strictly less than max_lon",
        )
    if min_lat >= max_lat:
        raise HTTPException(
            status_code=422,
            detail="min_lat must be strictly less than max_lat",
        )


def _run_inference(
    model: SegmentationModel, raw: bytes, bbox: tuple[float, float, float, float]
) -> tuple[Any, Any]:
    """Run one inference under the concurrency cap and a wall-clock timeout.

    The semaphore caps concurrent inferences so a burst of large tiles can't
    OOM the pod; extra requests wait up to 30s here instead of stacking
    ~500MB each. The submitted predict() runs on the bounded executor so a
    pathological tile that hangs the model cannot hold a semaphore slot
    forever — the client gets a 504 on timeout.
    """
    acquired = INFERENCE_SEMAPHORE.acquire(timeout=30)
    if not acquired:
        raise HTTPException(
            status_code=503,
            detail="Server is at capacity; retry after a short delay",
        )
    try:
        future = _TIMEOUT_POOL.submit(model.predict, raw, bbox=bbox)
        try:
            return future.result(timeout=INFERENCE_TIMEOUT)
        except concurrent.futures.TimeoutError as exc:
            # cancel() is a no-op once the predict is running (the worker is
            # already mid-inference), so the abandoned thread keeps working and
            # the executor reclaims the worker when it finishes. The semaphore
            # permit is still released by the finally below, so a single stuck
            # tile degrades to a 504 rather than permanently exhausting a slot.
            future.cancel()
            raise HTTPException(
                status_code=504,
                detail=f"Inference did not complete within {INFERENCE_TIMEOUT}s",
            ) from exc
    finally:
        INFERENCE_SEMAPHORE.release()


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
        fg_prob, transform = _run_inference(
            model, raw, bbox=(min_lon, min_lat, max_lon, max_lat)
        )
        # Only the buildings postprocessor exists today. A future task
        # (e.g. roads) would dispatch to mask_to_linestrings here, so the
        # branch is dropped rather than left unreachable.
        features = mask_to_polygons(fg_prob, transform, threshold=threshold)
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
    threshold: Annotated[float, Query(ge=0.0, le=1.0)] = 0.5,
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
