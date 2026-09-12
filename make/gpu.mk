# Included by the top-level Makefile. Target grouping: GPU plumbing.
# Everything here is opt-in via NARS_GPU=1 — see the Makefile variable block.

GPU_NODE_DIR    ?= nars-infra/docker
GPU_DEVICE_DIR  ?= nars-infra/k8s/nvidia-device-plugin
GPU_OVERLAY_DIR ?= nars-infra/segma-gpu

# ─── Preflight ──────────────────────────────────────────────────

# Internal: refuse to proceed with GPU plumbing unless the host really can
# donate a GPU. Checks are cheap and non-destructive.
.PHONY: _gpu-preflight-host
_gpu-preflight-host:
	@if [ "$(NARS_GPU)" != "1" ]; then exit 0; fi
	@for dev in nvidiactl nvidia-modeset nvidia-uvm nvidia-uvm-tools nvidia0; do \
		if [ -e "/dev/$$dev" ]; then continue; fi; \
		echo "✖ NARS_GPU=1 but /dev/$$dev is missing — is the NVIDIA kernel module loaded?"; \
		exit 1; \
	done; \
	echo "✓ Host GPU device nodes present"

.PHONY: _gpu-preflight
_gpu-preflight: _gpu-preflight-host ## GPU preflight checks (NARS_GPU=1 only)
	@if [ "$(NARS_GPU)" != "1" ]; then exit 0; fi
	@git check-ignore $(GPU_DRIVER_DIR) >/dev/null 2>&1 \
		|| echo "  ⚠ $(GPU_DRIVER_DIR) is inside the repo — add it to .gitignore (see docs)";
	@echo "✓ GPU preflight done (driver bundle is built by cluster-create/gpu-install)"

# ─── Host driver bundle ─────────────────────────────────────────

.PHONY: gpu-driver-bundle
gpu-driver-bundle: ## Curate the host NVIDIA driver libs into $(GPU_DRIVER_DIR) (idempotent)
	@echo "→ Building curated NVIDIA driver bundle..."
	@nars-infra/scripts/nvidia-driver-bundle.sh "$(GPU_DRIVER_DIR)"

# ─── Node image ─────────────────────────────────────────────────

.PHONY: gpu-node-image
gpu-node-image: ## Build the kind GPU node image (toolkit + CDI boot hook)
	@echo "→ Building GPU node image $(GPU_NODE_IMAGE)..."
	@docker build -t "$(GPU_NODE_IMAGE)" \
		-f "$(GPU_NODE_DIR)/Dockerfile.nars-gpu-node" nars-infra

# ─── Deploy ─────────────────────────────────────────────────────

.PHONY: gpu-install
gpu-install: _gpu-preflight ## Install CDI device plugin + apply the segma GPU overlay
	@# Nodes bind the bundle at /opt/nvidia/driver; make sure it exists even if
	@# cluster-create was skipped (e.g. gpu-install on an existing cluster).
	@[ -d "$(GPU_DRIVER_DIR)/lib64" ] || $(SUBMAKE) gpu-driver-bundle
	@echo "→ Installing NVIDIA device plugin..."
	@$(KUBECTL) apply -k "$(GPU_DEVICE_DIR)"
	@echo "→ Applying segma GPU overlay (nvidia.com/gpu: 1)..."
	@$(KUBECTL) apply -k "$(GPU_OVERLAY_DIR)"
	@echo "→ Rolling nars-segma..."
	@$(KUBECTL) rollout restart deployment/nars-segma -n "$(NAMESPACE)"
	@echo "→ Waiting for plugin + segma..."
	@$(KUBECTL) -n kube-system rollout status daemonset/nvidia-device-plugin --timeout=120s >/dev/null || true
	@$(KUBECTL) -n "$(NAMESPACE)" rollout status deployment/nars-segma --timeout=180s || true
	@echo "✓ GPU plumbing installed"

.PHONY: gpu-status
gpu-status: ## Show GPU availability (plugin pod, node allocatable, segma device)
	@echo "=== Device plugin ==="
	@$(KUBECTL) -n kube-system get pods -l k8s-app=nvidia-device-plugin -o wide
	@echo "=== Node GPU allocatable ==="
	@$(KUBECTL) get nodes -o jsonpath='{.items[*].status.allocatable.nvidia\.com/gpu}{"\n"}'
	@echo "=== segma pod ==="
	@$(KUBECTL) -n "$(NAMESPACE)" get pod -l app.kubernetes.io/name=nars-segma \
		-o jsonpath='{.items[*].status.containerStatuses[0].name}{": "}{.items[*].status.containerStatuses[0].state}' 2>/dev/null; echo ""

.PHONY: gpu-smoke-test
gpu-smoke-test: ## Run a one-shot torch CUDA smoke Job on the GPU
	@echo "→ Running torch CUDA smoke job (nars-segma image, 1 GPU)..."
	@# Single shared GPU: scale segma down so the smoke job can schedule (the
	@# image is preloaded into the node, so IfNotPresent — not Always).
	@# .ONESHELL runs this whole recipe in ONE shell, so an EXIT trap restores
	@# segma even if a step below fails (set -e) — same pattern as db-ef-migrate's
	@# port-forward cleanup. Restore first, then trap armed for the last step.
	@restore_segma() { \
		$(KUBECTL) scale deployment/nars-segma -n "$(NAMESPACE)" --replicas=1 >/dev/null 2>&1 || true; \
		$(KUBECTL) rollout status deployment/nars-segma -n "$(NAMESPACE)" --timeout=240s >/dev/null 2>&1 || true; \
	}; \
	trap restore_segma EXIT; \
	$(KUBECTL) scale deployment/nars-segma -n "$(NAMESPACE)" --replicas=0 >/dev/null 2>&1 || true
	@# A Job is deterministic (catches the fast-exiting torch one-liner), unlike
	@# kubectl run --rm -i --attach which races pod lifecycle / keep-alives.
	@$(KUBECTL) apply -k nars-infra/k8s/gpu-smoke
	@# Gate the target on the job reaching Complete: a broken GPU (torch exit
	@# code + Failed job) then fails this target instead of a silent no-op. The
	@# EXIT trap restores segma regardless; logs still dump either way. Note a
	@# quickly-failing job waits out the full timeout below — wait only polls
	@# for the Complete condition, and a Failed condition stops it at timeout.
	@wait_rc=0; \
	$(KUBECTL) wait --for=condition=Complete job/nars-gpu-smoke -n "$(NAMESPACE)" --timeout=120s >/dev/null 2>&1 || wait_rc=$$?; \
	$(KUBECTL) logs job/nars-gpu-smoke -n "$(NAMESPACE)" 2>&1 || true; \
	$(KUBECTL) delete job nars-gpu-smoke -n "$(NAMESPACE)" --wait=false >/dev/null 2>&1 || true; \
	if [ "$$wait_rc" -ne 0 ]; then \
		echo "✖ gpu smoke job did not complete (kubectl wait exit $$wait_rc) — see logs above"; \
		exit 1; \
	fi
	@echo "✓ GPU smoke test passed (job nars-gpu-smoke completed)"
