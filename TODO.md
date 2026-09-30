# NARS TODO

## Deployment robustness

Three defects were fixed on 2026-09-28 while bringing a cold cluster up; they
are recorded because the first two were silent and could have been mistaken for
a GPU/hardware problem:

- [x] `nvidia.com/gpu` sat as a *sibling* of `limits`/`requests` in
      `nars-infra/segma/deployment.yaml` (since `3d7b894`). `ResourceRequirements`
      only defines `limits`/`requests`/`claims`, so the API server rejected the
      Deployment with a confusing `strict decoding error: unknown field
      ...resources.nvidia.com/gpu`. It went unnoticed for ~3 commits because the
      already-running pod kept serving while every fresh apply failed. Verified
      with a server-side dry-run of both placements: nested is accepted, sibling
      is not. Fixed in `ca42d91`.
- [x] `nars-limits` LimitRange capped containers at 2 CPU / 4Gi, so the segma
      limit increase to 8 CPU / 12Gi was rejected at admission ("maximum cpu
      usage per Container is 2, but limit is 8"), and the namespace ResourceQuota
      would have been exceeded too. Both raised for 4 segma replicas on the
      96-core host. A quota is a ceiling, not a reservation, so the 16-CPU dev
      kind node is unaffected. Fixed in `7938949`.
- [x] `make cluster-up` on a *cold* cluster applied GPU manifests before the
      device plugin had advertised the extended resource; `gpu-install` only
      waited for the DaemonSet rollout and ended in `|| true`. The kubelet
      registers the resource asynchronously, so plugin Ready != resource
      advertised. `make/gpu.mk` now polls node allocatable and fails with a
      pointer to `make gpu-status`. Fixed in `60e0089`.

Two rootless-runtime fixes were needed on 2026-09-30 while deploying `nars-tiles`
to the rootless kind cluster (host rootless Docker, uid-mapped). Both presented
as an identical, hard-to-read symptom — nginx `[emerg] ... failed (13: Permission
denied)` at boot — and BOTH stem from the same underlying rule: *under a
uid-mapped runtime, a directory Debian pre-chowns to a service user
(`www-data`) is unwritable by the container-root process, because container
uid 0 maps to a different host sub-uid than the `www-data` owner.* They are
recorded because the first fix looked complete (fcgiwrap spawned) yet the pod
kept crash-looping on the next writable path:

- [x] `/run/fcgiwrap` — image + entrypoint used to `chown www-data:www-data`, so
      creating the socket/pidfile as root hit EACCES. The dir is now kept
      root-owned; `spawn-fcgi` chowns JUST the socket (0660), which is all the
      www-data nginx workers need to connect. `mapserver-entrypoint.sh` +
      `Dockerfile.nars-tiles`.
- [x] `/var/log/nginx` — even after the above, nginx's master (uid 0) reopens
      `error.log`/`access.log` after dropping workers to www-data, and the dir is
      www-data:adm. Runtime `chown root:root` did NOT survive the pod's user
      namespace. Robust fix: nginx logs to `/dev/stderr` + `/dev/stdout` (inherited
      fds work under any uid mapping), via a committed overlay of Debian's stock
      `nginx.conf` (`nars-infra/docker/nginx.conf`). The entrypoint chown was
      then removed so it cannot become a boot-time landmine.

Operational lessons from the same deploy, worth internalising:

- `docker inspect` `.Id` (config digest) ≠ the `imageID` containerd reports for a
  pod (manifest digest for the OCI index). Do not chase "stale image" theories
  from this mismatch alone — `crictl inspecti` + build timestamp are the ground
  truth. (A long "the pod must be running the old image" detour here was exactly
  that mistake.)
- `kind load docker-image`/`ctr -n k8s.io import` both register the tag; verify
  what the CRI will actually resolve with `crictl images` on the node before
  blaming the rollout.
- `make kustomize-apply` on an unchanged `latest`-tagged spec will NOT create a
  new ReplicaSet; `kubectl rollout restart deploy/<name>` (or scale 0→1) is
  required to pick up a newly loaded image.

Remaining:

- [x] Pin `IMAGE_TAG=<commit-sha>` instead of `latest` so a bad rollout is
      rollback-able. `docker.yml` already tags pushes `sha-<short>`; the
      deploy path refuses `latest` outside dev via `_check-pinned-tag`
      (gated on `kustomize-apply` and `images-push`, self-tested by
      `infra-lint-tag-guard`).
- [x] Add a server-side dry-run of the rendered manifests to CI. New
      `make infra-lint-k8s-dry-run` renders the overlay, boots a throwaway
      kind cluster, really applies the admission scaffold (namespace +
      LimitRange + ResourceQuota + PV), dry-runs the remainder server-side,
      and self-tests that the historical `resources.nvidia.com/gpu` sibling
      shape is rejected; wired as the `k8s-dry-run` CI job.

## National-scale rollout (58 wilayas / ~1,541 communes)

Capacity model, derived from the current code rather than guessed:
- A z18 chunk is capped at 24x24 tiles x 256px = 6144x6144 px, which segma
  decomposes at `NARS_SEGMA_TILE_SIZE=1024` into 36 windows.
- Bir Bouhouche = 3 chunks = 108 windows.
- **No end-to-end minutes/commune figure is trustworthy yet.** An earlier draft of
  this file asserted ~19 s/window -> ~34 min/commune -> ~36 days on one GPU.
  That chain is not supported: 19 s was a single synthetic HTTP observation, and
  a background-only `predict()` profiles at 1.55 s, a forward pass alone at
  0.40 s (FP32) / 0.05 s (FP16). Service-level timings measured on the RTX 2060
  for a *synthetic* 1024px grid, warm cache: roads 2.0 s, buildings 3.2 s
  (roads 13.4 s on first call). None of these is a real commune tile, so treat
  every derived number below as provisional until a real-tile profile exists.

- [ ] Benchmark one real commune on the target GPU and record a true
      minutes/commune number. Every sizing decision below depends on it.
- [x] Build the async job pipeline. Shipped on 2026-09-30 as the Phase 1 road
      queue: `generation_jobs`/`generation_job_chunks` tables (`0002_create_generation_jobs.sql`),
      `GenerationJobController` (create → 202, `PATCH` as `POST .../cancel`,
      chunk raster upload, job GET), a worker pool (`GenerationJobWorker`,
      bounded concurrency + exponential backoff, `CancellationToken`-propagated
      shutdown), persisted progress, and resumable cancelled jobs (a cancelled
      job can be re-created from its already-uploaded draft ids via
      `CreateFromDraftIds`). The SPA drives it end to end:
      `nars-web/src/api/generation.ts` + a rewritten
      `nars-web/src/map/generate/generate-roads.ts` (enqueue → upload rendered
      chunks → poll → apply; cancels the mid-flight job if any upload fails).
      EF maps both tables with `ExcludeFromMigrations()` (SQL-created, like
      `ai_draft_features`); the fixes that made the EF migration clean are
      recorded under "Deployment robustness" below.
- [ ] Deploy the national tile server and cut over `satellite-tiler.ts` off the
      direct Esri World Imagery fetches (the nars-tiles server is committed and
      smoke-tested; deployment + `VITE_TILE_SATELLITE` cutover are tracked in
      "Satellite imagery" below). Current unauthenticated browser-side fetching
      would also rate limit at ~166k requests for a national backfill.
- [ ] Size the GPU fleet from measured throughput. Provisional arithmetic only:
      a 2-week backfill would need 2-4 GPUs and a 2-day one ~17-35, but both
      inherit the unvalidated s/window figure above. `nars-infra/segma/deployment.yaml`
      requests `nvidia.com/gpu: 1` at `replicas: 1` and is CUDA-only, so there is
      no CPU fallback for production.
- [x] Retune segma concurrency when scaling out. `MAX_CONCURRENT_INFERENCES`
      defaults to 2 per process (`nars-segma/app/main.py:67`), so N replicas means
      2N concurrent 1024 px prob maps against the pod's 4Gi limit. Now explicit
      and per-deployment: `overlays/production/patches/segma-scale.yaml` sets
      `NARS_SEGMA_MAX_CONCURRENT_INFERENCES=2` (conservative for the 12Gi limit);
      re-derive the value from measured wall-clock throughput at commissioning.
- [ ] Plan the data tier for ~2.8M draft geometries. Bir Bouhouche alone yielded
      1,835 drafts, so 1,541 communes extrapolates to ~2.8M drafts plus ~25k
      materialized roads. The 10Gi PVC in `nars-infra/k8s/postgis.yaml` is two
      orders of magnitude short: target 1-2TB NVMe, 128GB+ RAM, and real HA
      Postgres instead of `replicas: 1`. Budget for GIST index build time.
- [x] Scale `nars-api` for concurrent map serving. Done for production via the
      HPA patch (`patches/api-scale.yaml`, 4-8 replicas);

## Production server bring-up (post-purchase)

Target hardware: 2U rackmount, 1x 96-core single socket (raised from 64 to
co-schedule the imagery tile serving with the GPU fleet on the same box), 256GB
ECC DDR5, 4x NVIDIA L4 24GB, 4x 3.84TB NVMe Gen4 in RAID10, 2x 10GbE, dual PSU,
UPS.
Four L4s would put a full 1,541-commune backfill at ~9 days versus ~36 on one
GPU, but both numbers scale from the same unvalidated s/window estimate and must
be re-derived once a real commune is profiled end to end.
Co-scheduling tile serving alongside the workers pushes concurrent demand to
roughly 64-72 cores, so 64 would leave no headroom for the throughput the GPU
fleet is meant to deliver. Note this is now static-file serving (nginx over a
pre-built pyramid, see "Satellite imagery" below), which is far lighter on CPU
and memory than the GeoServer JVM originally assumed here.

Do these before or during commissioning. Left unchanged, the current limits
leave most of the box idle.

- [x] Raise the segma CPU limit. `nars-infra/segma/deployment.yaml` now budgets
      ~8 CPU per replica (requests 2 / limits 8), up from the old 2 CPU / 4Gi;
      with 4 replicas that is 32 cores of the 96-core host. Note this
      is an unvalidated lever, not a measured one: profiling the roads pipeline
      on a *background-only* synthetic tile put the forward pass at 0.4s and
      postprocess at 0.01s, so postprocess cost on a real dense road tile is
      still unmeasured. Profile on a real z18 tile before treating the CPU
      budget as the throughput driver.
- [x] Raise the segma memory limit to 8-12Gi per replica. `nars-infra/segma/deployment.yaml`
      now sets limits 12Gi (requests 4Gi). A 1024 px window peaks
      around 500 MB and `MAX_CONCURRENT_INFERENCES=2`
      (`nars-segma/app/main.py:67`) is per process, so 4 replicas means 8
      concurrent prob maps.
- [x] Run 4 segma replicas with one GPU each. `overlays/production/patches/segma-scale.yaml`
      sets `replicas: 4` (dev keeps the base `replicas: 1` for the single-GPU
      dev node). Each replica requests `nvidia.com/gpu: 1`, so on the target
      single-node 4×L4 box the device plugin gives one L4 per replica and a
      CPU-only/single-GPU host leaves the extras Pending (fail-closed). Node
      affinity is unnecessary on the single-node target; cross-node scheduling
      is handled by the device plugin. `MAX_CONCURRENT_INFERENCES` re-derivation
      is the commissioning-time measurement, not a build-time constant.
- [ ] Benchmark one real commune on the target L4 before the PO lands. Every
      capacity figure so far extrapolates a single ~19 s/window measurement taken
      on an unknown dev GPU with concurrency 2 unsaturated, and the whole
      4-GPU sizing rests on it.
- [ ] Finish validating FP16 autocast before enabling it. Implemented behind
      `NARS_SEGMA_AMP`, defaulting off, with CUDA-only FP16 in
      `_predict_tile`. Measured on the real 65MB checkpoint and a real GPU:
      7.1x on the forward pass (0.396s -> 0.053s per 1024px window, FP16 timed
      first to rule out warm-up bias) and excellent agreement, max |delta|
      2.6e-4 over 8.4M pixels. What is NOT yet proven: no available sample
      drives the model above 0.05 probability, so the 0.6 floor boundary was
      never exercised. The floor is applied to per-piece mean confidence, so a
      ~2.6e-4 per-pixel perturbation can only move a piece's score by about
      that much, and only pieces scoring within ~2.6e-4 of 0.6 can flip - but
      that is an estimate, not a measurement. Re-run against a real z18 tile
      once imagery exists and compare generated geometry FP32-vs-FP16.
- [ ] Reconcile the per-window cost. The forward pass is ~0.4s on an RTX 2060
      and a full `predict()` on a background-only tile is ~1.55s, but the
      earlier end-to-end HTTP measurement was ~19s per window. Those do not
      reconcile, and the difference decides whether the GPU, the CPU postprocess,
      or request/queue overhead is the real bottleneck. Profile a real request
      on a real tile before buying GPUs for throughput.
- [ ] Move PostGIS onto the new array and size the volume for the national
      dataset. `nars-infra/k8s/postgis.yaml` ships `replicas: 1` with a 10Gi
      PVC, two orders of magnitude short. Tune as a starting point for 256GB
      host RAM: `shared_buffers=32GB`, `effective_cache_size=192GB`,
      `work_mem=64MB`, `maintenance_work_mem=2GB`. Treat these as a starting
      point to measure, not fixed values.
- [ ] Budget GIST index build time and space for ~2.8M draft geometries before
      the first backfill, not during it. An unindexed or half-built spatial index
      will stall both the 1.1 m dedup in `nars-api` and the async worker pool.
- [x] Scale `nars-api` to 4-8 replicas (`nars-infra/k8s/app-deployment.yaml:12`
      is `replicas: 2`) for concurrent map serving under national data volumes.
      `overlays/production/patches/api-scale.yaml` raises the `nars-api` HPA to
      `minReplicas: 4` / `maxReplicas: 8` (dev HPA stays 2-4); the deployment
      base stays `replicas: 2` since the HPA owns the count.
- [ ] Automate off-site backups and test a restore. On-host data survives
      cluster deletion but not disk loss, and this is a single server with no
      replica. A backup that has never been restored is not a backup.
- [ ] Plan HA as a second node plus managed Postgres. One box holding national
      production data has no failover; the four L4s can migrate to a second node
      without changing the design.
- [x] Pin image digests rather than `latest` before this reaches production.
      Non-dev deploys (`DEPLOY_ENV != dev`) now dereference the pinned
      `IMAGE_TAG` to its registry **digest** at apply time:
      `nars-infra/scripts/resolve-image-digests.sh` (top-level index digest via
      `docker buildx imagetools inspect`) feeds
      `nars-infra/scripts/kustomize-digest-rewrite.awk`, which rewrites every
      container `image:` to `org/name@sha256:...` and FAILS CLOSED if any image
      has no digest (no mutable-tag fallback). Dev keeps `latest` for fast
      iteration. Guarded by `make infra-lint-digest-guard` (offline fixture
      tests, wired into `infra-lint`); verified end-to-end against a local
      registry (index + single-manifest images).

## Satellite imagery (nginx XYZ tiles)

Decisions taken: z18 over urban extents only, bulk ingest of a fixed mosaic,
co-scheduled on the NARS server, internal/company users only. This replaces the
direct Esri World Imagery fetches and removes the third-party dependency from
the async pipeline.

Storage stays within the 4x 3.84TB RAID10 above. z18 over urban extents is
~0.5-0.7TB, so the earlier sizing concern is retired. A national z18 pyramid
would have been ~8-15TB and is deliberately not what we are building.

**GeoServer is settled — the tile-server code is committed and smoke-tested.**
Serve a pre-built `gdal2tiles --xyz` pyramid from nginx for the hot path and
re-render WMS on demand with MapServer (cgi-mapserver + fcgiwrap) for the
`/data` sources. The reasoning that retired GeoServer:

- The tile path is static file serving. `gdal2tiles.py -t xyz` already writes
  exactly `{z}/{x}/{y}.png` at 256px in EPSG:3857, which is the contract
  `satellite-tiler.ts` consumes. A JVM would only be reading files off disk.
- GeoServer could not be made to publish a single layer here. Store config was
  accepted (HTTP 201) but resource enumeration always returned empty, and
  publishing returned HTTP 500 `Failed to create reader from null`
  (`gce.geotiff: Argument "input" should not be null`). Reproduced on
  `kartoza/geoserver` 3.0.1, 2.26.1 and 2.28.5.
- It was not a raster/GDAL problem: a vector Shapefile store failed identically,
  so the catalog itself is at fault. Ruled out along the way: JSON REST request
  bodies (`CannotResolveClassException`, writes must use XML), the `datastores`
  vs `coveragestores` distinction, and a GDAL/GeoTools version skew (2.26.1
  pairs GDAL 3.0.4 with GeoTools 32.1; 2.28.5 pairs 3.8.4 with 34.5, and both
  fail). File access itself is fine - the tif is readable as the `geoserveruser`
  process.
- Heap cost is not justified. The plan below budgeted 8-16GB of JVM heap while
  the `nars-limits` LimitRange caps any container at 12Gi. Static files need
  none of it.

Implementation, all committed and verified by `make tiles-smoke-test`:

- `nars-infra/docker/Dockerfile.nars-tiles` — debian trixie with nginx-light +
  cgi-mapserver + fcgiwrap. `/tiles/{z}/{x}/{y}.png` is served statically
  (nginx never touches GDAL); `/wms` is proxied to MapServer (GetCapabilities,
  GetMap, mode=tile). MapServer ships with a global `mapserver.conf` because
  Debian's cgi-mapserver requires `MS_MAP_PATTERN`.
- Data layout: `/data/tiles` = the XYZ pyramid, `/data/sources` = clipped
  EPSG:3857 rasters + the gdaltindex GPKG that feeds WMS re-render.
- Ingest pipeline (written; NOT yet run against real imagery):
  `nars-infra/scripts/build_imagery_pyramid.sh` (clip to the `areas` union →
  `gdalwarp -t_srs EPSG:3857` → gdaladdo overviews → `gdal2tiles --xyz -r
  bilinear` → gdaltindex with absolute paths → coverage gate → opt-in dated
  rotation) plus `nars-infra/scripts/check_tile_coverage.py` (per-commune z18
  assertion, fails loudly on gaps).
- Smoke test `nars-infra/scripts/tiles_smoke_test.sh` asserts: static 256x256
  PNG + CORS Origin reflection, deleted-tile 404, GetCapabilities advertising
  the layer + templated onlineresource, whole-world GetMap, and mode=tile.
- Frontend plumbing is done: `VITE_TILE_SATELLITE` build-arg threaded through
  the Makefile and GitHub Actions (passed ONLY when non-empty — the config's
  `??` means an empty arg would override the Esri default), plus CSP allow-lists
  in `nars-web/index.html`, `nginx.nars-vite.conf`, `appsettings.json` and
  `AppOptions.cs`.
- CI: docker.yml builds+pushes `nars-tiles` (paths-filter lockstep with the
  image-guard glob set); ci.yml runs the tile smoke job.

What this gives up, and the trigger to revisit: no OGC WMTS, no
`REQUEST=GetTile`, no transparent-overlay tile pipeline. MapServer `mode=tile`
covers every client we actually have (MapLibre uses it). If WMTS becomes a real
requirement, add MapCache in front of the existing pyramid (static files,
no JVM) — not GeoServer.

Raster sources, because they serve different purposes:
- z10-14 national (~50-120GB) for basemap display across the whole country.
- z18 urban (~0.5-0.7TB) for segmentation input, clipped to the union of the
  `areas` urban polygons, which is exactly the clip geometry the ingest needs.

- [ ] Confirm the imagery licence permits internal republication as tiles.
      Internal-only use sidesteps public redistribution, but the upstream terms
      still have to allow it. Do this before the bulk ingest, not after. This
      blocks every item below that mentions "run".
- [x] Tile server image + configs (`Dockerfile.nars-tiles`, `mapserv.conf`,
      `xyz.map`, `mapserver.conf`, `mapserver-entrypoint.sh`) — end-to-end
      smoke-tested via `make tiles-smoke-test`.
- [x] Ingest + coverage scripts written (`build_imagery_pyramid.sh`,
      `check_tile_coverage.py`); refusing to run until `CONFIRM_IMAGERY_LICENCE`
      is set.
- [x] Frontend plumbing: `VITE_TILE_SATELLITE` build-arg (Makefile + GH Actions,
      empty-arg-safe) and CSP allow-lists (index.html / nginx / appsettings /
      AppOptions) + wwwroot synced.
- [x] CI: nars-tiles path-filter + build/push in docker.yml; tile smoke job in
      ci.yml.
- [ ] Run the ingest against the real national mosaic (licence + data first):
      `make tiles-pyramid-build IMAGERY_INPUT=... AREAS_GPKG=... MAX_ZOOM=18`,
      then inspect the z10-14 national basemap and the z18 urban pyramid.
- [x] Add a tiles Deployment under `nars-infra/k8s/`: `tiles.yaml` (PVC +
      Deployment + ClusterIP Service), `tiles-pv.yaml` (kind hostPath PV,
      statically bound like postgis), `ingress-tiles.yaml` (mTLS, host
      `tiles.nars.dz`), `service-account.yaml` (nars-tiles),
      `network-policy.yaml` (`allow-tiles-from-ingress-and-segma`), production
      patches (`remove-tiles-pv.yaml`, `storage-tiles-pvc.yaml`), base
      kustomization `images:` entry + `nars-tiles` in `SCALABLE_DEPLOYS`.
      Server-space: nginx serves the XYZ pyramid from the PVC + MapServer WMS
      (fcgiwrap) with the CORS headers already in the nginx conf. Pod
      securityContext mirrors the container's supervise-nginx+fcgiwrap contract
      (runAsUser 0, drop ALL + CHOWN/SETGID/SETUID/NET_BIND_SERVICE —
      baseline-PSA-clean; restricted warns the same way PostGIS's init does).
- [ ] Set `VITE_TILE_SATELLITE` (CI `vars` or `TILE_SATELLITE_URL`) to the tile
      XYZ template when the server is up (`nars-web/src/config/index.ts:57`).
      The code substitutes `{z}/{x}/{y}` by name, so no frontend change is
      needed. Until then the Esri default applies.
- [x] Expose tiles on two paths: nginx-ingress with the existing mTLS
      (`nars-infra/k8s/ingress-api.yaml:25`) for the browser, and ClusterIP-only
      with no ingress for the segma worker, which never leaves the cluster and
      so needs neither TLS nor a client cert. `ingress-tiles.yaml` serves
      `/tiles/` + `/wms` on `tiles.nars.dz`; the `tiles` ClusterIP Service is
      the in-cluster path, opened to nars-segma by the network policy.
- [ ] Port `renderSatelliteGrid` to server-side GDAL. Canvas compositing is a
      browser API, so an async worker needs its own equivalent to assemble the
      georeferenced 6144x6144 JPEG. Serving our own imagery does not remove this
      work.
- [x] Parallelize the tile fetch in `satellite-tiler.ts`. The chunk loop now pulls
      tiles through `mapWithConcurrency` (16 in flight), replacing the old 576
      serial round-trips per 24x24 chunk (`TILE_FETCH_CONCURRENCY`,
      `satellite-tiler.ts:42`).
- [ ] Re-cut the pyramid when source imagery updates, rather than re-rendering
      per request. Static tiles are all-or-nothing per build, which is an
      acceptable trade for a fixed pre-decided mosaic. Keep the previous pyramid
      on disk until the new one passes the coverage assertion above, so a bad
      ingest is a rollback rather than an outage.

