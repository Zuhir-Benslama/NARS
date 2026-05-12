# TODO

## Infrastructure — Cluster & Data

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

## Field Worker Role — Backend (NARS API) ✅ DONE

- [x] FieldWorker user role + hierarchy
- [x] Inspection system (model, controller, DTOs, migration)
- [x] Feature scoping by commune

## Field Worker Role — Frontend (nars-vite) ✅ DONE

- [x] FieldPanel, inspection forms, map click handler
- [x] Store, types, i18n, role-based routing

## Field Worker Role — Android (NARStreet) ✅ DONE

- [x] Role field, inspection API methods

## Admin Dashboard

- [x] National admin overview with scrollable wilaya grid
- [x] Wilaya drill-down with daira/commune breakdown

## nars-vite (Web frontend)

- [x] Integrate logging service

## Infrastructure

- [x] Push Docker images to registry
- [x] Deploy to k8s cluster (`make cluster-up`)
- [x] CI/CD pipeline

## Observability (LGTM Stack) ✅ DONE

- [x] Install kube-prometheus-stack (Prometheus + Grafana + AlertManager)
- [x] Install Loki (log aggregation)
- [x] Install Tempo (distributed tracing)
- [x] Install OpenTelemetry Collector
- [x] Configure Grafana datasources (Loki, Tempo, Prometheus)
- [x] Instrument NARS backend — traces (AspNetCore, HttpClient, EF Core) + metrics (Runtime, Hosting, Kestrel)
- [x] Instrument nars-vite frontend — traces (page load, fetch) via OTel Web SDK
- [x] Configure OTel pipelines: traces → Tempo, metrics → Prometheus, logs → Loki
- [x] Add ServiceMonitor for Prometheus scraping of OTel Collector

## Dashboard Capabilities ✅ DONE

- [x] Include field workers in admin overview stats (backend + frontend)
- [x] Add totals summary row per commune in stats tables
- [x] Add inline user creation button from dashboard

## User Type Test Coverage ✅ DONE

- [x] Add `UserRolesTests` — `field_worker` is not admin, `IsCommuneScoped` checks
- [x] Add `AdminControllerIntegrationTests` — all create-role combos tested (incl. commune\_user → field\_worker)
- [x] Add `AdminControllerIntegrationTests` — overview for every admin role + forbid for non-admins
- [x] Add `AdminControllerIntegrationTests` — wilaya drill-down and daira drill-down with scope enforcement

## Future / Nice-to-have ✅ DONE

- [x] Install metrics-server for HPA autoscaling
- [x] Add `docs/seed_reference_data.sql` to init process for fresh deployments
- [x] Generate proper EF migration to capture pending model changes
- [x] Replace hostPath PV with CSI-backed persistent volume for production
- [x] Add database backup cronjob in k8s
