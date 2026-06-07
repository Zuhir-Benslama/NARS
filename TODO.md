# TODO

Per-project TODO files:

| Project | File |
|---------|------|
| NARS (.NET Backend) | [`NARS/TODO.md`](NARS/TODO.md) |
| nars-vite (Vue 3 Frontend) | [`nars-vite/TODO.md`](nars-vite/TODO.md) |
| NARStreet (Android) | → moved to [`Zuhir-Benslama/NARStreet`](https://github.com/Zuhir-Benslama/NARStreet) |

---

## Cross-Project (Infrastructure, CI, Deployments) ✅ DONE

- [x] **P0 — Make integration tests a required CI gate**: Run `NARS.Tests/Integration` in CI (PostgreSQL + PostGIS via Testcontainers), publish results, and block merge on failures.
- [x] **P0 — Remove bootstrap default credentials**: Replace documented static admin password with one-time generated credentials (`make db-admin`) and update docs so no reusable defaults are shown.
- [x] **P1 — Add deterministic cluster bootstrap + smoke test**: Keep `make cluster-up` fully idempotent with robust waits/retries and add a post-deploy smoke target that verifies `/health`, API auth, and frontend reachability.
- [x] **P1 — Enforce stricter quality gates**: Treat backend warnings as errors in CI and enforce frontend minimum test coverage threshold so quality regressions fail fast.
- [x] **P2 — Repo hygiene + supply-chain guardrails**: Remove accidental directories/artifacts, keep generated files untracked, and enable automated dependency/security checks (Dependabot + CodeQL + secret scanning).

### Infrastructure — Cluster & Data
- [x] Migrate from Docker Compose to k3s/kind cluster
- [x] Rename postgres → postgis across all k8s manifests
- [x] Build and push `nars-postgis` image (postgis/postgis:17-3.5 base)
- [x] Add PostGIS extension creation to init script
- [x] Add `MigrateAsync()` to Program.cs for auto-applying EF migrations
- [x] Suppress `PendingModelChangesWarning` in DbContext config
- [x] Fix PostgreSQL liveness/readiness probes timeout (1s → 5s)
- [x] Load reference data (58 wilayas, 557 dairas, 1541 communes)
- [x] Create `create_national_admin.sh` bootstrap script
- [x] Fix PostgreSQL WAL corruption after hard shutdown (`pg_resetwal`)
- [x] Push Docker images to registry
- [x] Deploy to k8s cluster (`make cluster-up`)
- [x] CI/CD pipeline

### Observability (LGTM Stack)
- [x] Install kube-prometheus-stack (Prometheus + Grafana + AlertManager)
- [x] Install Loki (log aggregation)
- [x] Install Tempo (distributed tracing)
- [x] Install OpenTelemetry Collector
- [x] Configure Grafana datasources (Loki, Tempo, Prometheus)
- [x] Instrument NARS backend — traces (AspNetCore, HttpClient, EF Core) + metrics (Runtime, Hosting, Kestrel)
- [x] Instrument nars-vite frontend — traces (page load, fetch) via OTel Web SDK
- [x] Configure OTel pipelines: traces → Tempo, metrics → Prometheus, logs → Loki
- [x] Add ServiceMonitor for Prometheus scraping of OTel Collector

### Feature Work
- [x] Field Worker role (backend + frontend + Android)
- [x] Admin Dashboard (overview + wilaya/daira drill-down)
- [x] Dashboard inline user creation
- [x] User role test coverage

### Future / Nice-to-have
- [x] Install metrics-server for HPA autoscaling
- [x] Add `docs/seed_reference_data.sql` to init process for fresh deployments
- [x] Generate proper EF migration to capture pending model changes
- [x] Replace hostPath PV with CSI-backed persistent volume for production
- [x] Add database backup cronjob in k8s
