# NARS Roads TODO

Code quality issues found during review of the segmentation microservice
(FastAPI + PyTorch inference). Grouped by severity.

## Review round 2 (current)

New findings from a fresh review (tests: 17 passed, 6 skipped without
torch/skimage; `ruff check` clean). All fixed.

## High

- [x] **nars-roads can never become Ready in the cluster — the service is unusable as deployed** (`nars-infra/roads/deployment.yaml`, `nars-infra/docker/Dockerfile.nars-roads:44`)
  - The `/ready` fail-closed gate is correct, but nothing ever provisions weights: `weights/` contains only `.gitkeep` (empty dir baked into the image), and the deployment mounts no weights volume / initContainer / ConfigMap / PVC.
  - So `SegmentationModel.__init__` (missing `weights/unet_r34_multiclass.pth`) leaves `is_loaded=False` → `/ready` returns 503 forever → the readiness probe never passes → the pod is stuck NotReady and `nars-api`'s draft-feature queue (commit `f3da3d6`) always fails via `SegmentationClient.cs:64`.
  - **Fix:** mount real weights (volume or baked into the image at CI time) and/or add an initContainer; document the expected source of the checkpoint.
  - ✅ **Done:** `fetch-weights` initContainer downloads the checkpoint from `NARS_ROADS_WEIGHTS_URL` (secret key `weights-url`) into a shared emptyDir at `/srv/weights`; the main container reads it via `NARS_ROADS_WEIGHTS_PATH`. The init fails fast (CrashLoopBackOff with a clear message) instead of sitting NotReady when weights can't be fetched. Makefile `secrets-apply` now writes the `weights-url` key; `.env.example` documents the new var.

## Medium

- [x] **Bbox query params are unvalidated** (`app/main.py:92-96`)
  - `min_lon > max_lon` or out-of-range values (lat > 90, lon > 180) flow into `from_bounds` (`app/model.py:174`) producing an inverted transform → silently garbage GeoJSON coordinates.
  - **Fix:** validate `min < max` and ±90/±180 ranges → 422 otherwise.
  - ✅ **Done:** `_validate_bbox` rejects inverted/out-of-range/degenerate boxes with 422 (4 new tests).

- [x] **No concurrency limiting on a single 4Gi pod** (`app/main.py:85-149`)
  - `/segment` is a sync `def` → FastAPI threadpool (up to ~40 concurrent by default). Each inference allocates `probs` (25M×3×4 = 300MB) + `counts` (100MB) + windows. ~40 concurrent near-max tiles × ~500MB ≈ 18GB ≫ 4Gi limit → OOM kill under load.
  - **Fix:** bound concurrency with a small `threading.BoundedSemaphore` (e.g. 2-4) around `_model.predict`.
  - ✅ **Done:** `INFERENCE_SEMAPHORE` (2 permits) wraps inference; extra requests queue in the threadpool (test asserts bounded).

## Low

- [x] **Float-raster rescale heuristic is fragile** (`app/model.py:113-114`)
  - `if img.max() > 1.0: img = img / 255.0` — a float tile with max slightly above 1.0 (noise/sensor artifacts) gets divided by 255 → near-black input. A single `NaN`/`Inf` propagates through the `max()` check and Imagenet normalization → NaN predictions, unchecked downstream.
  - **Fix:** treat values ≤ ~255 as byte-scaled only when clearly in `[0, 255]`, and guard non-finite values.
  - ✅ **Done:** only rescale when `max > 2.0` (byte-scaled); `nan_to_num` neutralizes NaN/±Inf before normalization (3 new tests).

- [x] **Token compare is not constant-time** (`app/main.py:58`)
  - `x_internal_token != INTERNAL_TOKEN` — internal network so exposure is low, but `secrets.compare_digest` is free.
  - **Fix:** `secrets.compare_digest(x_internal_token, INTERNAL_TOKEN)`.
  - ✅ **Done:** `secrets.compare_digest` + fail-closed when token env is empty (2 new tests).

- [x] **`SegmentResponse.features` is untyped dicts** (`app/schemas.py:8`)
  - The declared `response_model` only validates that `features` is a list of dicts, not the GeoJSON structure — the "response_model now validates" TODO claim is overstated.
  - **Fix:** accept as documented pass-through, or model the Feature with `type`/`geometry`/`properties` fields.
  - ✅ **Done:** `Feature`/`FeatureGeometry` models; response validates structure end to end (2 new schema tests; end-to-end test now asserts geometry type/coordinates/confidence).

## High (round 1)

- [x] **Unsafe `torch.load` — pickle RCE vector** (`app/model.py:42`)
  - `torch.load(weights_path, map_location=self.device)` — torch 2.4.1 (pinned) defaults `weights_only=False`, so a checkpoint is an arbitrary-pickle payload. If weights are ever updated from an untrusted source, this executes code.
  - **Fix:** `torch.load(..., weights_only=True)`.

- [x] **Auth fails open when the token is unset** (`app/main.py:38`)
  - `if INTERNAL_TOKEN and x_internal_token != INTERNAL_TOKEN` — if `NARS_ROADS_INTERNAL_TOKEN` is missing (broken/missing secret), the guard is a no-op and the service runs with no authentication.
  - **Fix:** fails closed — `if not INTERNAL_TOKEN or x_internal_token != INTERNAL_TOKEN`.

- [x] **Sync inference inside an `async` handler blocks the event loop** (`app/main.py:52-85`)
  - `model.predict`, `mask_to_linestrings`, `mask_to_polygons` are CPU-bound and run directly in `async def segment`. With the default single uvicorn worker, one inference blocks everything including `/health`, so long tiles can trip the liveness probe and kill the pod mid-batch.
  - **Fix:** endpoint is now a plain `def`, so FastAPI runs it in the threadpool.

- [x] **No upload size limit — memory exhaustion** (`app/main.py:72`)
  - `raw = await tile.read()` reads the whole tile into memory with no cap before `rasterio.open`. A huge TIFF inside the cluster can OOM a pod at the 4Gi limit.
  - **Fix:** reads at most `MAX_TILE_BYTES + 1` bytes (default 50 MiB, `NARS_ROADS_MAX_TILE_BYTES` env) and rejects larger uploads with 413.

## Medium (round 1)

- [x] **16-bit/float imagery is mis-scaled** (`app/model.py:83`)
  - `if img.max() > 1.0: img = img / 255.0` — a 16-bit GeoTIFF (common for aerial/satellite) has max up to 65535; dividing by 255 leaves values up to ~257 (garbage for Imagenet normalization).
  - **Fix:** integer rasters are scaled by bit depth (`uint8 → /255`, `uint16 → /65535`); float input is kept as-is unless it is in [0, 255].

- [x] **Inconsistent exception handling in mask→vector conversion** (`app/postprocess.py:47` vs `:88-91`)
  - Roads path: `line.simplify()` / `mapping(line)` are unguarded — one pathological skeleton edge 500s the whole request.
  - Polygons path: `except Exception: continue` silently drops features.
  - **Fix:** both paths now guard geometry ops in try/except, skip the feature, and log a debug message instead of crashing or vanishing silently.

- [x] **Pod marked ready while serving garbage predictions** (`app/main.py:42`, `nars-infra/roads/deployment.yaml:43`)
  - `/health` reports `model_loaded` but the readiness probe only checks HTTP 200. When weights are missing, the service logs a warning and serves random predictions while appearing ready.
  - **Fix:** new `/ready` endpoint returns 503 until weights are loaded; the readiness probe now targets `/ready`. Liveness stays on `/health`.

- [x] **`threshold` query param is not range-validated** (`app/main.py:58`)
  - `threshold: float = 0.5` accepts any value; `-1` or `2` silently changes output semantics.
  - **Fix:** validated `0 <= threshold <= 1` (422 otherwise).

## Low (round 1)

- [x] **`response_model` declared but bypassed** (`app/main.py:47-91`)
  - Returning a raw `JSONResponse` skips Pydantic response validation; `SegmentResponse` only documents. Either validate through it or drop it.
  - **Fix:** the endpoint now returns a `SegmentResponse` instance, so FastAPI validates and serializes through the declared model.

- [x] **Eager model load at import time** (`app/main.py:31`)
  - `SegmentationModel(...)` runs at module import — slow cold start, re-loads on every `--reload`. Consider a lazy/lifespan init.
  - **Fix:** model is built in the FastAPI `lifespan` context and held in module state; importing the app no longer loads weights.

- [x] **Full raster read into RAM** (`app/model.py:71`)
  - `src.read()` loads the entire raster before deciding anything (compounds the upload-size issue).
  - **Fix:** `predict` now decodes the raster one tile-sized window at a time (never the full image), and a decoded-pixel budget (`MAX_DECODED_PIXELS`, 25M px) is enforced before the output arrays are allocated, so a tiny highly-compressible TIFF can't decompress into gigabytes. Oversized decodes raise `TileTooLargeError` -> HTTP 413.

- [x] **Typing gaps**
  - `SegmentationModel.predict` has no return annotation (`app/model.py:116`); `health()` returns an untyped dict.
  - **Fix:** annotated `predict`, `_read_image`, `_preprocess`, `_predict_tile`, `health`, `ready`, `segment`.

- [x] **No tests**
  - `.dockerignore` excludes `tests/` and none exist. The raster→mask→vector pipeline (grid stitching, transform mapping, thresholding) is testable without the model.
  - **Fix:** `tests/` now covers the API contract (auth fail-closed, size caps, validation, /health, /ready, end-to-end /segment), the mask→vector converters, and the model (dtype scaling, windowed reads, transform mapping, decode budget). Run with `make roads-test` (pytest in the Python 3.11 image) or `pytest` locally where torch/skimage aren't installed — torch/skimage-dependent tests skip automatically.
