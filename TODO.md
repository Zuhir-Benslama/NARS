# NARS TODO

## Deployment robustness

- [ ] Pin `IMAGE_TAG=<commit-sha>` instead of `latest` so a bad rollout is
      rollback-able. `latest` is mutable and the previous image gets garbage-
      collected, leaving no rollback path. `make deploy` already warns about
      this; make the non-dev deploy pipeline require a pinned tag
      (guards exist in `make/images.mk`: `_check-pinned-tag` / `_warn-latest-tag`).

## National-scale rollout (58 wilayas / ~1,541 communes)

Capacity model, derived from the current code rather than guessed:
- A z18 chunk is capped at 24x24 tiles x 256px = 6144x6144 px, which segma
  decomposes at `NARS_SEGMA_TILE_SIZE=1024` into 36 windows.
- Bir Bouhouche = 3 chunks = 108 windows. Measured ~19 s/window on an
  unsaturated dev GPU, so ~34 min per commune and ~1.5 communes/hour.
- 1,541 communes x 34 min = ~873 h, i.e. ~36 days of continuous time on one GPU.

- [ ] Benchmark one real commune on the target GPU and record a true
      minutes/commune number. Every sizing decision below depends on it.
- [ ] Build the async job pipeline. This is the real blocker, not hardware:
      the flow is synchronous browser -> nars-api -> segma, and
      `NARS_SEGMA_INFERENCE_TIMEOUT` is capped at 3600 s
      (`nars-segma/app/main.py:75`), so an average ~5 h wilaya job cannot fit in
      a request. Nothing can resume, so a failure at hour 4 redoes hours 1-4.
      Needs per-chunk enqueue, a worker pool, persisted progress, resumable
      checkpoints and UI progress. No queue exists today (no BullMQ/agenda/Celery).
- [ ] Integrate the national tile server, replacing the direct Esri World Imagery
      fetches in `nars-web/src/map/generate/satellite-tiler.ts`, called from
      `generate-roads.ts:179`. Current unauthenticated browser-side fetching would
      also rate limit at ~166k requests for a national backfill.
- [ ] Size the GPU fleet from measured throughput. ~2 weeks for a full backfill
      needs 2-4 GPUs; ~2 days needs ~17-35. `nars-infra/segma/deployment.yaml`
      requests `nvidia.com/gpu: 1` at `replicas: 1` and is CUDA-only, so there is
      no CPU fallback for production.
- [ ] Retune segma concurrency when scaling out. `MAX_CONCURRENT_INFERENCES`
      defaults to 2 per process (`nars-segma/app/main.py:67`), so N replicas means
      2N concurrent 1024 px prob maps against the pod's 4Gi limit.
- [ ] Plan the data tier for ~2.8M draft geometries. Bir Bouhouche alone yielded
      1,835 drafts, so 1,541 communes extrapolates to ~2.8M drafts plus ~25k
      materialized roads. The 10Gi PVC in `nars-infra/k8s/postgis.yaml` is two
      orders of magnitude short: target 1-2TB NVMe, 128GB+ RAM, and real HA
      Postgres instead of `replicas: 1`. Budget for GIST index build time.
- [ ] Scale `nars-api` for concurrent map serving. Data tier is now sized; the
      concrete replica and tuning changes are in "Production server bring-up" below.

## Production server bring-up (post-purchase)

Target hardware: 2U rackmount, 1x 96-core single socket (raised from 64 when
GeoServer was added to the same box), 256GB ECC DDR5, 4x NVIDIA L4 24GB,
4x 3.84TB NVMe Gen4 in RAID10, 2x 10GbE, dual PSU, UPS.
Four L4s put a full 1,541-commune backfill at ~9 days versus ~36 on one GPU.
Co-scheduling GeoServer pushes concurrent demand to roughly 64-72 cores, so 64
would leave no headroom for the throughput the GPU fleet is meant to deliver.

Do these before or during commissioning. Left unchanged, the current limits
leave most of the box idle.

- [ ] Raise the segma CPU limit. `nars-infra/segma/deployment.yaml` caps
      `nars-segma` at 2 CPU / 4Gi, and with 4 replicas that is 8 cores of a
      96-core host, under 10% utilization. Budget ~8 CPU per replica. Note this
      is an unvalidated lever, not a measured one: profiling the roads pipeline
      on a *background-only* synthetic tile put the forward pass at 0.4s and
      postprocess at 0.01s, so postprocess cost on a real dense road tile is
      still unmeasured. Profile on a real z18 tile before treating the CPU
      budget as the throughput driver.
- [ ] Raise the segma memory limit to 8-12Gi per replica. A 1024 px window peaks
      around 500 MB and `MAX_CONCURRENT_INFERENCES=2`
      (`nars-segma/app/main.py:67`) is per process, so 4 replicas means 8
      concurrent prob maps under the old 4Gi ceiling.
- [ ] Run 4 segma replicas with one GPU each, via node affinity or a
      `nvidia.com/gpu` label per node, rather than one replica contending for
      all four devices. Re-derive `MAX_CONCURRENT_INFERENCES` from measured
      throughput instead of keeping the default.
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
- [ ] Scale `nars-api` to 4-8 replicas (`nars-infra/k8s/app-deployment.yaml:12`
      is `replicas: 2`) for concurrent map serving under national data volumes.
- [ ] Automate off-site backups and test a restore. On-host data survives
      cluster deletion but not disk loss, and this is a single server with no
      replica. A backup that has never been restored is not a backup.
- [ ] Plan HA as a second node plus managed Postgres. One box holding national
      production data has no failover; the four L4s can migrate to a second node
      without changing the design.
- [ ] Pin image digests rather than `latest` before this reaches production. See
      "Deployment robustness" above.

## GeoServer and satellite imagery

Decisions taken: z18 over urban extents only, bulk ingest of a fixed mosaic,
GeoServer co-scheduled on the NARS server, internal/company users only. This
replaces the direct Esri World Imagery fetches and removes the third-party
dependency from the async pipeline.

Storage stays within the 4x 3.84TB RAID10 above. z18 over urban extents is
~0.5-0.7TB, plus a comparable GeoWebCache, so the earlier sizing concern is
retired. A national z18 pyramid would have been ~8-15TB and is deliberately not
what we are building.

Raster stores, because they serve different purposes:
- z10-14 national (~50-120GB) for basemap display across the whole country.
- z18 urban (~0.5-0.7TB) for segmentation input, clipped to the union of the
  `areas` urban polygons, which is exactly the clip geometry the ingest needs.

- [ ] Confirm the imagery licence permits internal republication as tiles.
      Internal-only use sidesteps public redistribution, but the upstream terms
      still have to allow it. Do this before the bulk ingest, not after.
- [ ] Build the ingest pipeline: pull the mosaic from the national imagery API
      for urban extents, clip to the `areas` union, reproject to EPSG:3857, then
      build the z18 XYZ pyramid offline with GDAL `gdal2tiles.py -t xyz -r bilinear`.
      Build offline; do not have GeoServer generate tiles.
- [ ] Add GeoServer manifests under `nars-infra/`. This is greenfield, nothing
      exists today. Give the JVM 8-16GB heap, not the whole machine, and let the
      remaining RAM be page cache for the pyramid.
- [ ] Configure GeoWebCache with a pre-seeded disk cache over the same extent, a
      `TileLayer` disk quota, and a trash buffer around 10% so the cache cannot
      grow unbounded. Dynamic rendering only as a fallback.
- [ ] Configure CORS on the GeoServer XYZ endpoint. `satellite-tiler.ts` sets
      `img.crossOrigin = "anonymous"` then calls `ctx.drawImage` and
      `canvas.toBlob`, so a missing `Access-Control-Allow-Origin` taints the
      canvas and throws a `SecurityError`. Esri sent these headers; GeoServer
      will not unless configured.
- [ ] Publish GeoServer EPSG:3857 XYZ at 256px. `lonToTileX`/`latToTileY` in
      `satellite-tiler.ts` are standard slippy math, so any other tiling scheme
      forces frontend changes.
- [ ] Expose GeoServer on two paths: nginx-ingress with the existing mTLS
      (`nars-infra/k8s/ingress-api.yaml:25`) for the browser, and ClusterIP-only
      with no ingress for the segma worker, which never leaves the cluster and
      so needs neither TLS nor a client cert.
- [ ] Set `VITE_TILE_SATELLITE` to the GeoServer XYZ template
      (`nars-web/src/config/index.ts:57`). This is the whole frontend cutover;
      the code substitutes `{z}/{x}/{y}` by name, so no frontend change is needed.
- [ ] Assert per-commune z18 coverage before any backfill, and fail loudly on
      gaps. A coverage gap is the dangerous case: `gdal2tiles` simply emits
      nothing there, so the model would silently receive lower-resolution input,
      which is exactly the "few roads" regression already debugged once.
- [ ] Port `renderSatelliteGrid` to server-side GDAL. Canvas compositing is a
      browser API, so an async worker needs its own equivalent to assemble the
      georeferenced 6144x6144 JPEG. GeoServer unblocks the imagery source but
      does not remove this work.
- [ ] Parallelize the tile fetch in `satellite-tiler.ts`. It awaits one tile at a
      time in a nested double loop, which is 576 sequential round-trips per 24x24
      chunk. Acceptable against a CDN edge, a real drag against our own
      GeoServer. Batch it 8-16 concurrent.
- [ ] Treat GeoServer as an added SPOF on an already single-server deployment. A
      pre-seeded local GeoWebCache survives a GeoServer restart, which softens
      but does not remove the exposure.

