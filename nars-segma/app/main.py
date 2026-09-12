"""
NARS Segmentation Service
Stateless inference microservice: satellite/aerial tile in -> GeoJSON draft
features out. One model instance per feature type (a model registry) rather
than a shared multiclass network, so each checkpoint can be swapped and
released independently. The registry currently has `buildings` (polygons) and
`roads` (linestrings), each with its own endpoint (/segment/buildings and
/segment/roads) so a response never mixes feature types.

This service owns no data. nars-api (.NET) is responsible for persisting
results into ai_draft_features and for auth/business rules. This service
is only reachable from inside the cluster network.
"""

import concurrent.futures
import itertools
import logging
import os
import secrets
import threading
from collections.abc import Callable
from contextlib import asynccontextmanager
from typing import Annotated, Any, TypedDict, TypeVar

import numpy as np
import rasterio
from fastapi import (
    Depends,
    FastAPI,
    Header,
    HTTPException,
    Query,
    Request,
    UploadFile,
)

from app.config import env_float, env_int
from app.model import (
    InvalidTileError,
    SegmentationModel,
    TileTooLargeError,
)
from app.postprocess import mask_to_linestrings, mask_to_polygons
from app.schemas import Feature, FeatureCollection, SegmentResponse

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("nars-segma")


# Shared-secret for cluster-internal requests. Read once at import time: a
# runtime rotation of the env var (e.g. via a pod restart with a new mounted
# secret) is applied on the next process start, not on the fly. This is the
# intended model for a static, cluster-internal service.
INTERNAL_TOKEN = os.environ.get("NARS_SEGMA_INTERNAL_TOKEN")
TILE_SIZE = env_int("NARS_SEGMA_TILE_SIZE", 1024, minimum=16, maximum=8192)
# Upper bound on a single tile upload. Inference reads the whole tile into
# memory, so an oversized upload is a pod-level memory-exhaustion risk.
MAX_TILE_BYTES = env_int(
    "NARS_SEGMA_MAX_TILE_BYTES",
    50 * 1024 * 1024,
    minimum=1024,
    maximum=1024 * 1024 * 1024,
)
# How many inferences may run concurrently before requests queue in the
# threadpool. Sized for the pod memory limit; raise/lower per deployment.
MAX_CONCURRENT_INFERENCES = env_int(
    "NARS_SEGMA_MAX_CONCURRENT_INFERENCES", 2, minimum=1, maximum=64
)
# Wall-clock ceiling on a single predict() call.  A pathological tile that
# hangs the model would otherwise hold a semaphore slot indefinitely; with
# MAX_CONCURRENT_INFERENCES=2 just two such tiles exhaust all capacity.
INFERENCE_TIMEOUT = env_int(
    "NARS_SEGMA_INFERENCE_TIMEOUT", 120, minimum=1, maximum=3600
)
# How long a request waits for an inference slot before failing with 503.
# Requests park on this (their upload stays spooled on disk), not on a per-
# request inference buffer.
QUEUE_TIMEOUT = env_int("NARS_SEGMA_QUEUE_TIMEOUT", 30, minimum=0, maximum=300)

# Road rules (limitation): discard edges below these thresholds before they
# reach the API. The minimum length forbids the stub spurs and debris slivers
# that the skeletonizer emits at tile edges; the minimum confidence drops weak
# detections; the per-tile cap bounds how many drafts one acceptance run can
# create. Separation is structural (each edge is already one junction-to-
# junction segment) and needs no tuning.
ROAD_MIN_LENGTH_M = env_float(
    "NARS_SEGMA_ROAD_MIN_LENGTH_M", 0.0, minimum=0.0, maximum=10000.0
)
ROAD_MIN_CONFIDENCE = env_float(
    "NARS_SEGMA_ROAD_MIN_CONFIDENCE", 0.0, minimum=0.0, maximum=1.0
)
ROAD_MAX_FEATURES = env_int(
    "NARS_SEGMA_ROAD_MAX_FEATURES", 0, minimum=0, maximum=100000
)

# Building rules (limitation): same shape as the road rules, applied to the
# polygon extraction. A minimum confidence drops ghost rooftops; the per-tile
# cap bounds the number of building drafts one acceptance run can produce.
BUILDING_MIN_CONFIDENCE = env_float(
    "NARS_SEGMA_BUILDING_MIN_CONFIDENCE", 0.0, minimum=0.0, maximum=1.0
)
BUILDING_MAX_FEATURES = env_int(
    "NARS_SEGMA_BUILDING_MAX_FEATURES", 0, minimum=0, maximum=100000
)


# Model registry: feature type -> how to build its model. Each entry is an
# independent binary (foreground/background) checkpoint, so one task can be
# updated or rolled back without touching the others. `builder` picks the
# network architect (smp-unet for the smp.ResNet34 U-Net buildings baseline,
# resnet34-upsample for the SpaceNet 3 champion roads architecture in
# app/road_model.py); `postprocess` picks how the foreground mask becomes
# features (polygons for buildings, linestrings/centerlines for roads).
class ModelSpec(TypedDict):
    """How to construct one task's SegmentationModel."""

    weights_path: str
    num_classes: int
    builder: str
    postprocess: str


MODEL_SPECS: dict[str, ModelSpec] = {
    "buildings": {
        "weights_path": os.environ.get(
            "NARS_SEGMA_WEIGHTS_PATH", "weights/unet_bldg_base.pth"
        ),
        "num_classes": 2,
        "builder": "smp-unet",
        "postprocess": "polygons",
    },
    "roads": {
        "weights_path": os.environ.get(
            "NARS_SEGMA_ROAD_WEIGHTS_PATH", "weights/roads_best.pth"
        ),
        "num_classes": 1,
        "builder": "resnet34-upsample",
        "postprocess": "linestrings",
    },
}

# Foreground mask -> vector conversion per registered task. Kept separate
# from MODEL_SPECS so the two concerns (model choice vs output geometry) can
# evolve independently.
POSTPROCESSORS: dict[str, Callable[..., list[Feature]]] = {
    "polygons": mask_to_polygons,
    "linestrings": mask_to_linestrings,
}

# Built lazily in lifespan so importing this module (tests, tooling, --reload)
# never forces a multi-hundred-MB weight load. Protected by _models_lock for
# safe concurrent reads during request handling and writes during lifecycle
# transitions.
_models: dict[str, SegmentationModel] = {}
_models_lock = threading.Lock()

# Each inference can peak at ~500MB (25M-px prob maps + windows), so unbounded
# threadpool concurrency on a 4Gi pod is an OOM risk. A small semaphore caps
# how many inferences run in parallel; the rest wait for a permit (see
# _segment_task) before their upload is even buffered, so they park disk, not RAM.
INFERENCE_SEMAPHORE = threading.BoundedSemaphore(MAX_CONCURRENT_INFERENCES)


T = TypeVar("T")


class _InferenceExecutor:
    """A fresh daemon thread per submitted callable.

    ThreadPoolExecutor's workers are non-daemon AND joined at interpreter exit
    by its atexit handler, so a predict abandoned by the 504 timeout path — a
    running Python thread cannot be interrupted — would stall pod termination
    for the whole k8s grace period. Daemon threads are not joined on exit:
    SIGTERM then tears the pod down immediately while the abandoned inference
    is still running.

    A fixed pool has a second, worse failure mode: the worker running the
    abandoned predict is gone forever, so sustained pathological tiles
    exhaust the pool and later requests 504 even though the model would have
    finished quickly. A fresh thread per submit means an abandoned inference
    costs only the one thread (it drains and exits on its own) and the next
    request is never starved. Running-predict concurrency is still bounded —
    not by this executor but by INFERENCE_SEMAPHORE, which caps acquisition
    to MAX_CONCURRENT_INFERENCES regardless of how many threads were spawned.
    """

    _serial = itertools.count(1)

    def __init__(self, thread_name_prefix: str = "infer-timeout"):
        self._thread_name_prefix = thread_name_prefix

    def submit(self, fn: Callable[[], T]) -> concurrent.futures.Future[T]:
        """Run `fn` on a fresh daemon thread and return its Future. The
        thread is created on demand, so importing this module (tests, tooling,
        --reload) never spawns threads."""
        future: concurrent.futures.Future[Any] = concurrent.futures.Future()
        thread = threading.Thread(
            target=self._run,
            args=(future, fn),
            name=f"{self._thread_name_prefix}-{next(self._serial)}",
            daemon=True,
        )
        thread.start()
        return future

    @staticmethod
    def _run(future: concurrent.futures.Future[Any], fn: Callable[[], Any]) -> None:
        if not future.set_running_or_notify_cancel():
            return
        try:
            future.set_result(fn())
        # Any failure of a user-supplied callable must land in the Future
        # so the awaiting request sees it; the broad catch is deliberate.
        except Exception as exc:  # noqa: BLE001 - propagate to the future
            future.set_exception(exc)


# Runs each predict() on a fresh daemon thread so the 504 timeout can abandon
# a hung inference without ever permanently losing a worker (see
# _InferenceExecutor). Concurrency is bounded by INFERENCE_SEMAPHORE, not by
# this pool.
_TIMEOUT_POOL = _InferenceExecutor()


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
            builder=spec["builder"],
        )
    except Exception:  # a load failure must never abort startup
        logger.exception(
            "Failed to load weights for task '%s'; it will be unavailable", task
        )
        return None


@asynccontextmanager
async def lifespan(_app: FastAPI):
    with _models_lock:
        for task, spec in MODEL_SPECS.items():
            model = _load_model(task, spec)
            if model is not None:
                _models[task] = model
    yield
    with _models_lock:
        _models.clear()


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
    with _models_lock:
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
    for name, value, low, high in [
        ("min_lon", min_lon, -180.0, 180.0),
        ("max_lon", max_lon, -180.0, 180.0),
        ("min_lat", min_lat, -90.0, 90.0),
        ("max_lat", max_lat, -90.0, 90.0),
    ]:
        if not (low <= value <= high):
            raise HTTPException(
                status_code=422, detail=f"{name} must be in [{low}, {high}]"
            )
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
) -> tuple[np.ndarray, rasterio.Affine]:
    """Run one predict on a fresh daemon thread under a wall-clock timeout.

    The submitted predict() runs on a thread the timeout can abandon (see
    _InferenceExecutor): a pathological tile that hangs the model cannot hold
    an inference slot forever — the client gets a 504 on timeout, the
    abandoned thread keeps working until it finishes and then exits, and the
    next request gets a fresh thread instead of a permanently lost worker.
    """
    future = _TIMEOUT_POOL.submit(lambda: model.predict(raw, bbox=bbox))
    try:
        return future.result(timeout=INFERENCE_TIMEOUT)
    except concurrent.futures.TimeoutError as exc:
        # cancel() is a no-op once the predict is running (the thread is
        # already mid-inference), so the abandoned thread keeps working and
        # exits when it finishes. The inference permit is released by the
        # caller's finally, so a single stuck tile degrades to a 504 rather
        # than permanently exhausting a slot.
        future.cancel()
        raise HTTPException(
            status_code=504,
            detail=f"Inference did not complete within {INFERENCE_TIMEOUT}s",
        ) from exc


def _read_upload(tile: UploadFile) -> bytes:
    """Read the upload under the authoritative size cap.

    Reads at most MAX_TILE_BYTES, so we never buffer an unbounded tile into
    memory. A file that is empty, or strictly larger than the cap, is
    rejected before any inference work happens."""
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
    return raw


def reject_oversized_request(request: Request) -> None:
    """Fast-fail on a declared multi-GB part before the body is buffered.

    _read_upload caps the bytes actually consumed, but by the time the
    endpoint runs Starlette has already buffered the whole multipart body
    (past ~1MB it spills from memory to the temp dir), so a client streaming
    a multi-GB part would fill the 2Gi emptyDir before the 413 fired. Running
    as a dependency, this rejects on the declared Content-Length before the
    UploadFile is parsed. The header is advisory (chunked or absent values
    bypass it), which is why the authoritative read-side cap in _read_upload
    stays."""
    declared = request.headers.get("content-length")
    if declared and declared.isdigit() and int(declared) > MAX_TILE_BYTES:
        raise HTTPException(
            status_code=413,
            detail=f"Tile exceeds the {MAX_TILE_BYTES} byte limit",
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
    _validate_bbox(min_lon, min_lat, max_lon, max_lat)

    if tile.content_type is None or tile.content_type not in (
        "image/tiff",
        "image/png",
        "image/jpeg",
    ):
        raise HTTPException(
            status_code=415, detail=f"Unsupported content type: {tile.content_type}"
        )

    # Cheap readiness gate before buffering the upload: an unready task should
    # reject with 503 without reading (and discarding) up to MAX_TILE_BYTES.
    with _models_lock:
        model = _models.get(task)
    if model is None or not model.is_loaded:
        raise HTTPException(status_code=503, detail=f"{task} model not ready")

    # Capacity gate before buffering the upload or allocating the prob maps:
    # at most MAX_CONCURRENT_INFERENCES requests hold a raw buffer + prob maps
    # at once. Extra requests wait up to QUEUE_TIMEOUT seconds here; their
    # uploads stay spooled on disk (Starlette), so a flood of large tiles
    # parks disk, not RAM.
    if not INFERENCE_SEMAPHORE.acquire(timeout=QUEUE_TIMEOUT):
        raise HTTPException(
            status_code=503,
            detail="Server is at capacity; retry after a short delay",
        )
    try:
        # The permit is held across the read, the inference AND the
        # postprocessing: mask_to_polygons allocates its own multi-hundred-MB
        # working sets (label/close/contours), so bounding vectorization under
        # the same permit keeps peak pod memory flat regardless of request
        # fan-out. A rejection below (empty/oversized upload) still releases
        # it in the finally.
        try:
            raw = _read_upload(tile)
            fg_prob, transform = _run_inference(
                model, raw, bbox=(min_lon, min_lat, max_lon, max_lat)
            )

            # Per-task postprocessing: polygons for buildings, centerline
            # linestrings for roads. Dispatch is driven by the registry so a
            # new task needs no code change here. Road rules (min length /
            # min confidence / per-tile cap) and building rules (min
            # confidence / cap) are read from the environment so a cadastre
            # convention change is a config bump, not a rebuild.
            postprocess = POSTPROCESSORS[MODEL_SPECS[task]["postprocess"]]
            if task == "roads":
                features = postprocess(
                    fg_prob,
                    transform,
                    threshold=threshold,
                    min_length_m=ROAD_MIN_LENGTH_M,
                    min_confidence=ROAD_MIN_CONFIDENCE,
                    max_features=ROAD_MAX_FEATURES or None,
                )
            elif task == "buildings":
                features = postprocess(
                    fg_prob,
                    transform,
                    threshold=threshold,
                    min_confidence=BUILDING_MIN_CONFIDENCE,
                    max_features=BUILDING_MAX_FEATURES or None,
                )
            else:
                features = postprocess(fg_prob, transform, threshold=threshold)
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
    finally:
        INFERENCE_SEMAPHORE.release()

    return FeatureCollection(features=features)


@app.post(
    "/segment/buildings",
    response_model=SegmentResponse,
    response_model_exclude_none=True,
    dependencies=[
        Depends(verify_internal_token),
        Depends(reject_oversized_request),
    ],
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


@app.post(
    "/segment/roads",
    response_model=SegmentResponse,
    response_model_exclude_none=True,
    dependencies=[
        Depends(verify_internal_token),
        Depends(reject_oversized_request),
    ],
)
def segment_roads(
    tile: UploadFile,
    min_lon: float,
    min_lat: float,
    max_lon: float,
    max_lat: float,
    threshold: Annotated[float, Query(ge=0.0, le=1.0)] = 0.5,
) -> SegmentResponse:
    """
    Run road inference on a single georeferenced tile.

    Uses the same contract as /segment/buildings (bbox as four separate query
    params) and returns road centerlines (LineString features) rather than
    building footprints (Polygons). A response contains roads only — feature
    types are never mixed. See the buildings docstring for the threading
    rationale (CPU-bound inference on a plain `def`).
    """
    return SegmentResponse(
        roads=_segment_task(
            "roads",
            tile,
            min_lon,
            min_lat,
            max_lon,
            max_lat,
            threshold,
        )
    )
