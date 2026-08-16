# NARS Roads TODO

Code quality issues found during review of the segmentation microservice
(FastAPI + PyTorch inference). Grouped by severity.

## Review round 6 (current)

Fresh review of the test suite on top of round 5. Baseline: mypy clean,
52 passed, 96.25% coverage (85% gate). All items resolved.

## Low

- [x] **Polygon skip-guards had no direct tests** (`app/postprocess.py`)
  - `mask_to_linestrings` got a deterministic monkeypatched-graph test for
    every degenerate guard (round 5), but the mirror guards in
    `mask_to_polygons` (short contour, missing contours, construction
    exception, invalid geometry) were only exercised via the happy path.
  - **Fix:** five new tests patch `skimage.measure.find_contours` (the
    function imports it locally, so it must be patched at the source) and
    `app.postprocess.Polygon` to force each guard, plus a mixed two-region
    test proving one degenerate region is skipped while the valid one is
    still emitted. Postprocess coverage 93% -> 100%.

- [x] **Dead `else` branch in `_segment_task`** (`app/main.py:205`)
  - The only caller passes the literal `"buildings"`, so the `else` -> 500
    for an unknown task was unreachable (0% coverage). The branch and its
    raise are gone; the direct `mask_to_polygons` call is noted as the
    future home of a roads dispatcher.

- [x] **Weights-loading happy path untested** (`app/model.py:85-90`)
  - `torch.load`/`load_state_dict`/`is_loaded=True` had no test; every test
    hit the weights-not-found branch. A torch-gated test saves the model's
    own state_dict to a tmp checkpoint and asserts a freshly constructed
    `SegmentationModel` reports `is_loaded` and is not the same net object.
    Model coverage 96% -> 100%.

- [x] **Warning noise from third-party libraries** (`pytest.ini`)
  - 19 warnings on every run: torch 2.4's `jit._script` deprecations
    (backtick-quoted message) and starlette's legacy `import multipart`
    `PendingDeprecationWarning`. Three `filterwarnings` entries (matched
    against the backtick-quoted torch messages) quiet the suite to zero.

- [x] **Auth token literal duplicated** (`tests/conftest.py`, `tests/helpers.py`,
    `tests/test_api.py`)
  - `"test-token"` lived in two files; changing one broke the other
    opaquely. `helpers.AUTH_TOKEN` is now the single source of truth:
    conftest sets `NARS_ROADS_INTERNAL_TOKEN` from it, test_api uses it in
    request headers.

## Verification

- `ruff check` / `ruff format --check` (with `ruff.toml`): clean.
- `mypy app/` (test image, full dependency stack): no issues in 5 files.
- Test image run: 58 passed, 0 warnings, 100.00% coverage (85% gate).

## Review round 5

Fresh review on top of round 4. All items resolved.

## Low

- [x] **Dead `# noqa: TRY003` directives** (`app/model.py`, `tests/test_api.py`)
  - ruff's default rule set does not include flake8-try, so the `TRY003`
    directives were unused (RUF100 would flag them) yet the raises they guard
    are intentional dynamic-message bypasses.
  - **Fix:** added `nars-roads/ruff.toml` with `extend-select = ["TRY"]`, making
    the directives live and meaningful instead of dead. The noqa comment on
    the `AssertionError` test stub was restored (TRY003 applies to any
    raise-with-message, not only raises inside `try`).

- [x] **No ruff/mypy configuration; type checking unenforced** (repo root)
  - nars-roads relied on ruff's defaults and nothing ran mypy: a type regression
    could slip through every gate.
  - **Fix:** `ruff.toml` (rule set documented, scoped to nars-roads/) and
    `mypy.ini` (`ignore_missing_imports = True` for the untyped scientific
    stack — torch, rasterio, shapely, sknw, skimage — while the app's own code
    stays fully checked). `mypy==2.1.0` added to `requirements-dev.txt`; the
    `Dockerfile.nars-roads` test image now runs `mypy app/` before pytest, so
    `make roads-test`/CI enforces it.

- [x] **`mask_to_linestrings` degenerate-edge guards untested**
    (`app/postprocess.py:43,55,58,61-63`)
  - The skip-paths (no `pts`, <2 points, zero-length line, invalid geometry,
    construction exception) were the only uncovered lines.
  - **Fix:** six new tests feed a monkeypatched sknw graph with crafted edges,
    covering each guard plus a mixed graph that proves degenerate edges are
    skipped while valid ones are still emitted. Note: shapely 2.x's
    `is_valid` does not reject self-crossing LineStrings (GEOS checks
    simplicity separately), so the `is_valid` guard is forced in its test.

- [x] **Georeferenced transform trusted without a CRS check**
    (`app/model.py`)
  - A projected-CRS tile (e.g. UTM, metre units) had its embedded transform
    trusted as-is, silently emitting meter-scale coordinates in an EPSG:4326
    GeoJSON response. A non-identity transform with no CRS was equally
    unverifiable.
  - **Fix:** `SegmentationModel._embedded_transform` trusts the tile's own
    transform only when the CRS is present and geographic (degree units);
    projected or absent CRS falls back to the caller-supplied bbox. New tests
    cover projected-CRS and missing-CRS tiles (fall back), and the
    georeferenced-EPSG:4326 tile is still trusted.

## Verification

- `ruff check` / `ruff format --check` (with the new `ruff.toml`): clean.
- `mypy app/` (with the new `mypy.ini`): no issues in 5 source files.
- `make roads-test` (test image): mypy clean, 52 passed, 96.25% coverage
  (85% gate).
- `hadolint` on the modified `Dockerfile.nars-roads`: clean.

## Review round 4

Fresh review on top of round 3 (ruff clean; 31 tests pass locally with the
torch/skimage-dependent tests skipped — full suite runs in the torch+skimage
test image). All items resolved.

## Medium

- [x] **`/segment` readiness gate was asymmetric with `/ready`** (`app/main.py`)
  - `/ready` returns 503 until real weights are loaded (`is_loaded`), but
    `/segment` only rejected when `_model is None` — a constructed-but-unloaded
    model (missing weights) served garbage predictions with HTTP 200 to any
    caller that bypassed the k8s probe, contradicting the documented fail-closed
    philosophy.
  - **Fix:** the endpoint gate now mirrors `/ready`:
    `if _model is None or not _model.is_loaded -> 503`. The end-to-end test
    flips `is_loaded = True` after lifespan (weights are random in the test
    image) so the full predict/postprocess pipeline is still exercised; a new
    `test_segment_rejects_unready_model` covers the 503 path.

## Low

- [x] **Hardcoded operational knobs** (`app/main.py`, `app/model.py`)
  - `INFERENCE_SEMAPHORE = BoundedSemaphore(2)` and `MAX_DECODED_PIXELS` were
    hardcoded, unlike every other knob (TILE_SIZE, MAX_TILE_BYTES).
  - **Fix:** `NARS_ROADS_MAX_CONCURRENT_INFERENCES` (default 2, clamped ≥ 1) and
    `NARS_ROADS_MAX_DECODED_PIXELS` (default 25,000,000) env vars.

- [x] **Garbage bytes with a TIFF content-type 500'd** (`app/model.py`, `app/main.py`)
  - `image/tiff` + non-image bytes reached rasterio, whose `RasterioIOError`
    fell into the broad `except Exception -> 500`. It is a client error.
  - **Fix:** `predict` wraps the decode in `except RasterioIOError` and raises
    `InvalidTileError`; the endpoint maps it to 400. New tests cover both the
    model-level raise and the endpoint's 400 mapping (and a generic
    inference failure still returns 500).

- [x] **`_normalize_window` took a dtype that could disagree with the data** (`app/model.py`)
  - It scaled by `src.dtypes[0]`; rasterio decodes every band into band 0's
    dtype, so the scale is now derived from the array's own dtype (`arr.dtype`),
    which can never disagree with the values being normalized.
  - **Fix:** dropped the `dtype` parameter; the docstring documents why
    `arr.dtype` is authoritative.

- [x] **`mask_to_linestrings` confidence sat outside the geometry guard** (`app/postprocess.py`)
  - The confidence read was outside the try/except that skips degenerate edges,
    so an unexpected indexing error there would 500 a request instead of
    skipping the feature.
  - **Fix:** moved the confidence computation inside the guarded block.

## Review round 3

## High

- [x] **`python-multipart==0.0.9` — CVE-2024-53981 DoS** (`requirements.txt:3`)
  - Affected `python-multipart` (`<0.0.18`) floods the event loop with a
    log-per-byte parse for malformed multipart boundaries (data before the
    first / after the last boundary), stalling the threadpool processing
    thread → DoS on a single request. `/segment` is a multipart endpoint.
    - **Fix:** `python-multipart==0.0.20` (>=0.0.18); FastAPI 0.115.0's
      `python-multipart>=0.0.7` constraint accepts it.
    - ⚠ Rebuild the image for this to take effect (`make roads-test` or
      `_build-nars-roads` re-run the pip layer). The verification runs below
      mounted current code over the old image.

## Medium

- [x] **Unready pod buffers the whole upload before rejecting** (`app/main.py:146`)
  - The `_model is None` gate ran *after* `tile.file.read(MAX_TILE_BYTES+1)`,
    so during model load every request still pulled up to 50 MiB into memory
    just to be told 503.
  - **Fix:** readiness gate moved before the read (cheap 503); content-type
    validation still runs first. Empty-file/oversize tests now stub `_model`
    so they still exercise their intended checks.

- [x] **Postprocessing runs outside the concurrency semaphore** (`app/main.py:167`)
  - The ~300 MB prob maps stay alive while `mask_to_linestrings`/
    `mask_to_polygons` allocate skeletonize/contour arrays on top of them,
    but the semaphore was released before those calls — peak memory could be
    2× predicts + 2× postprocesses simultaneously.
  - **Fix:** `predict` + both converters now share one permit.

## Low

- [x] **`verify_internal_token` annotated `str` with `None` default** (`app/main.py:60`)
  - Header is optional (rejected via `compare_digest` fail-closed); the
    annotation now reads `str | None`.

- [x] **`os.path.exists` before `torch.load`** (`app/model.py:66`)
  - A *directory* at the weights path passed `exists` and would crash the
    lifespan with a raw OSError. Now `os.path.isfile`.

- [x] **sknw `pts` indexed into the prob mask unguarded** (`app/postprocess.py:38`)
  - Float point coordinates from `sknw` would raise IndexError (→ 500) at
    the confidence computation, which sat outside the try/except. `pts` is
    now coerced to `np.intp` before use.

- [x] **Unused `client` fixture** (`tests/conftest.py:49`)
  - No test consumed it (test_api uses its module-level client); the fixture
    and its now-unused `pytest`/`TestClient`/`app` imports are gone.

- [x] **Test reads private `BoundedSemaphore._value`** (`tests/test_api.py:184`)
  - Replaced with a behavioral check: three non-blocking acquires must not
    all succeed (capacity ≤ 2), no private attribute access.

## Deferred (documented, not changed)

- **Dependency vintage:** `torch==2.4.1`, `fastapi==0.115.0`, `numpy==1.26.4`
  are 2024-era pins. `weights_only=True` neutralizes the main torch pickle
  RCE and the service is cluster-internal, so this is supply-chain hygiene
  rather than an active hole — recommend a coordinated bump in a dedicated
  pass (torch bumps can change inference numerics/behavior).
- **No per-request timeout:** a pathological raster can hold a semaphore
  permit indefinitely, queueing every other request. Accepted tradeoff —
  bounded memory under load matters more here; note in code is already
  present.
- **Float-raster rescale heuristic** (`max > 2.0` ⇒ byte-scaled): remains a
  heuristic; genuine float data in `(1, 255]` is still mis-scaled. Rare for
  this imagery and documented.

## Review round 2

- [x] **nars-roads can never become Ready in the cluster — the service is unusable as deployed** (`nars-infra/roads/deployment.yaml`, `nars-infra/docker/Dockerfile.nars-roads:44`)
  - The `/ready` fail-closed gate is correct, but nothing ever provisions weights: `weights/` contains only `.gitkeep` (empty dir baked into the image), and the deployment mounts no weights volume / initContainer / ConfigMap / PVC.
  - So `SegmentationModel.__init__` (missing `weights/unet_r34_multiclass.pth`) leaves `is_loaded=False` → `/ready` returns 503 forever → the readiness probe never passes → the pod is stuck NotReady and `nars-api`'s draft-feature queue (commit `f3da3d6`) always fails via `SegmentationClient.cs:64`.
  - **Fix:** mount real weights (volume or baked into the image at CI time) and/or add an initContainer; document the expected source of the checkpoint.
  - ✅ **Done:** `fetch-weights` initContainer downloads the checkpoint from `NARS_ROADS_WEIGHTS_URL` (secret key `weights-url`) into a shared emptyDir at `/srv/weights`; the main container reads it via `NARS_ROADS_WEIGHTS_PATH`. The init fails fast (CrashLoopBackOff with a clear message) instead of sitting NotReady when weights can't be fetched. Makefile `secrets-apply` now writes the `weights-url` key; `.env.example` documents the new var.
  - ✅ **Done (weights sourced):** no public checkpoint matches the original 3-class (background/road/building) `smp.Unet(resnet34, classes=3)` architecture. The service now uses a **model registry, one checkpoint per feature type** (`MODEL_SPECS` in `app/main.py`): buildings loads the HOT fAIr baseline `unet_bldg_base.pth` (https://huggingface.co/nilsho01/unet-resnet34-vhr-buildings, AGPL-3.0, 2-class, clean drop-in for `smp.Unet(resnet34, classes=2)`) and serves `/segment/buildings`. `SegmentationModel` is generic (num_classes param); `predict` returns `(fg_prob, transform)` (foreground = channel 1). The old `/segment` + always-empty `roads` response are gone; nars-api's `SegmentationClient`/`DraftFeaturesService` were updated to buildings-only (`SegmentSummaryResponse` no longer has RoadCount). Roads: add a `MODEL_SPECS` entry + a `/segment/roads` endpoint (postprocess `mask_to_linestrings`).

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
