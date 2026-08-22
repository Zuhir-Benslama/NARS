SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.ONESHELL:
.DEFAULT_GOAL := help

# Indirection for recursive make. Under .ONESHELL, make treats the whole
# recipe as a single line: if it contains $(MAKE), `make -n` (dry-run) actually
# EXECUTES the entire recipe instead of just printing it — which ran real
# `kubectl apply`/`kubectl wait`/`docker run` commands. Using $(SUBMAKE) keeps
# normal recursion (and the -j jobserver) intact but makes `-n` a true dry-run.
SUBMAKE := $(MAKE)

CLUSTER_NAME       ?= nars
NAMESPACE          ?= nars
DOMAIN             ?= nars.dz
APP_PORT           ?= 8080
APP_TLS_PORT       ?= 8443
K8S_DIR            ?= nars-infra/k8s
DOCKER_DIR         ?= nars-infra/docker
DOCKER_ORG         ?= zuhirbenslama
DOCKER_USERNAME    ?= zuhirbenslama
KUBECTL            ?= kubectl
DOCKER_TOKEN       ?=
BACKUP_DIR         ?= backup
DB_NAME            ?= nars_db
POSTGRES_DATA_DIR  ?= data/nars/postgis
REGISTRY_IMAGES    := nars-api nars-postgis nars-vite nars-backup nars-roads
SCALABLE_DEPLOYS   := postgis nars-api nars-frontend nars-roads
INGRESS_NGINX_VERSION ?= v1.12.0
METRICS_SERVER_VERSION ?= v0.7.2
LOCAL_PATH_PROVISIONER_VERSION ?= v0.0.30
# Helm chart versions are pinned for reproducible observability installs,
# consistent with the upstream-manifest and base-image pins above.
KUBE_PROMETHEUS_STACK_VERSION ?= 88.3.0
LOKI_VERSION ?= 7.3.0
TEMPO_VERSION ?= 1.24.4
OTEL_COLLECTOR_VERSION ?= 0.169.0
# Collector image tag is separate from the chart version — the chart's
# appVersion (0.158.0 for 0.169.0) does NOT track the pinned image tag.
# Kept as an explicit variable so `observability-otel-collector` and the
# helm-values file can't silently drift apart.
OTEL_COLLECTOR_IMAGE_TAG ?= 0.120.0
# Lint/proxy tool images — tag+digest pinned for reproducible linting, same
# convention as the k8s manifests. Digests verified against the named tags.
# NOTE: cytopia/yamllint publishes NO yamllint-version tags; major tag `1`
# is pinned by digest instead. hadolint/shellcheck were previously untagged/
# :stable (moving). alpine/socat was untagged (=latest) in two recipes.
YAMLLINT_IMAGE      ?= cytopia/yamllint:1@sha256:596fb19eb71e55ba5b2fa56d8c18a615ec82adc8d3bf2d73918cb78c8f3240fb
RUFF_IMAGE          ?= ghcr.io/astral-sh/ruff:0.15.15@sha256:ad419fa4f7d7ae24904bf18ce4c3a5e51538df83d11348e4c4a7f1e5d4a48207
NODE_IMAGE          ?= node:22-alpine@sha256:c610fcdfb1d5b4740dd70c284ed3cb16bb857e0f7166196e36a5501df7a3aa32
HADOLINT_IMAGE      ?= hadolint/hadolint:v2.15.1@sha256:32dac94127fd60b7b7e3fbfc65e1383b9b5e25c9bfd7b8536de7a539fe68a12d
SHELLCHECK_IMAGE    ?= koalaman/shellcheck:v0.11.0@sha256:61862eba1fcf09a484ebcc6feea46f1782532571a34ed51fedf90dd25f925a8d
SOCAT_IMAGE         ?= alpine/socat:1.8.1.3@sha256:3d9e7966201dd3a065df591020a09fd3c70845de7e7086e3531ea69db774406b
OBSERVABILITY_NAMESPACE ?= observability
LOG_DIR             ?= /tmp/nars
MIGRATIONS_DIR      ?= nars-infra/migrations
SCRIPTS_DIR         ?= nars-infra/scripts
POSTGIS_GET_POD_CMD = $(KUBECTL) get pod -n "$(NAMESPACE)" -l app.kubernetes.io/name=postgis -o jsonpath='{.items[0].metadata.name}' 2>/dev/null

# ─── Secrets ──────────────────────────────────────────────────
# Auto-generate .env with stable secrets if missing.
# Make auto-remakes missing included files and re-execs, so
# all targets see consistent POSTGRES_PASSWORD / JWT_SECRET.
# Generate a random base64 string using openssl (preferred) or python3.
# Used in recipe contexts (where $$1 is the byte count).
_rnd_cmd = if command -v openssl >/dev/null 2>&1; then \
	openssl rand -base64 "$$1" | tr -d '\n'; \
else \
	python3 -c "import base64,os; print(base64.b64encode(os.urandom(int(\"$$1\"))).decode())"; \
fi

# _RND shell function + $$(_RND N) expansion rely on .ONESHELL.
# umask 077 ensures the file is never readable by other users between
# creation and the explicit chmod below.
.env:
	@umask 077;
	@echo "# Auto-generated — DO NOT COMMIT" > $@;
	_RND() { $(_rnd_cmd); };
	echo "POSTGRES_PASSWORD=$$(_RND 32)" >> $@;
	echo "JWT_SECRET=$$(_RND 32)" >> $@;
	echo "GPG_PASSPHRASE=$$(_RND 32)" >> $@;
	echo "GRAFANA_PASSWORD=$$(_RND 12)" >> $@;
	echo "NARS_ADMIN_SIGNUP_TOKEN=$$(_RND 32)" >> $@;
	echo "NARS_ROADS_INTERNAL_TOKEN=$$(_RND 32)" >> $@;
	echo "NARS_ROADS_WEIGHTS_URL=$${NARS_ROADS_WEIGHTS_URL:-https://hf.co/nilsho01/unet-resnet34-vhr-buildings/resolve/main/unet_bldg_base.pth}" >> $@;
	chmod 600 $@;
	echo "→ Created $@ with fresh secrets (permissions: 600)"

-include .env

# Fallback values — only used if .env is missing and system has neither
# openssl nor python3 (unlikely on any modern OS).
# Empty defaults ensure secrets-protect targets fail fast rather than
# proceeding with guessable credentials.
POSTGRES_PASSWORD  ?=
JWT_SECRET         ?=
GPG_PASSPHRASE     ?=
GRAFANA_PASSWORD   ?=
NARS_ADMIN_SIGNUP_TOKEN ?=
NARS_ROADS_INTERNAL_TOKEN ?=
NARS_ROADS_WEIGHTS_URL ?=
export POSTGRES_PASSWORD JWT_SECRET GPG_PASSPHRASE GRAFANA_PASSWORD NARS_ADMIN_SIGNUP_TOKEN NARS_ROADS_INTERNAL_TOKEN NARS_ROADS_WEIGHTS_URL

.PHONY: help
help: ## Show available targets
	@grep -hE '^[a-zA-Z][a-zA-Z_-]*:.*?## .*$$' $(MAKEFILE_LIST) \
		| sort \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-28s\033[0m %s\n", $$1, $$2}'


# ─── Modular Sections ───────────────────────────────────────

# The targets below are split across make/*.mk for maintainability.
# GNU make reads all of them into a single instance, so variables,
# .ONESHELL, and cross-file prerequisites keep working unchanged.
-include make/*.mk

# ─── Convenience Targets ──────────────────────────────────────

.PHONY: all
all: cluster-up ## Bring up the full cluster (bare 'make' shows help)
