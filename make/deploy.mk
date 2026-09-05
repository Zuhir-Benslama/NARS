# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: k8s manifests, TLS, secrets, kustomize deploy.

# Kustomize build root, selected by DEPLOY_ENV (recursive = expanded at use time,
# after DEPLOY_ENV is set in make/images.mk).
#   dev (default) → development overlay (base + local dev ingresses)
#   anything else  → production overlay (Production env, no local ingresses) — fail-closed
K8S_OVERLAY_DIR = $(if $(filter dev,$(DEPLOY_ENV)),nars-infra/overlays/dev,nars-infra/overlays/production)


.PHONY: _check-secrets
_check-secrets: ## Fail fast if critical secrets are empty (prevents deploying with insecure defaults)
	@if [ -z "$(POSTGRES_PASSWORD)" ] || [ -z "$(JWT_SECRET)" ]; then
		echo "✖ Secrets not configured — run 'make .env' to generate them";
		exit 1;
	fi
	@if [ -z "$(GPG_PASSPHRASE)" ] || [ -z "$(GRAFANA_PASSWORD)" ]; then
		echo "✖ GPG_PASSPHRASE or GRAFANA_PASSWORD not set — run 'make .env' to generate them";
		exit 1;
	fi
	@if [ -z "$(NARS_ADMIN_SIGNUP_TOKEN)" ]; then
		echo "✖ NARS_ADMIN_SIGNUP_TOKEN not set — run 'make .env' to generate it";
		exit 1;
	fi
	@if [ -z "$(NARS_ROADS_INTERNAL_TOKEN)" ]; then
		echo "✖ NARS_ROADS_INTERNAL_TOKEN not set — run 'make .env' to generate it";
		exit 1;
	fi
	@if [ -z "$(NARS_ROADS_WEIGHTS_URL)" ]; then
		echo "✖ NARS_ROADS_WEIGHTS_URL not set — the roads pod's fetch-weights initContainer would never become ready";
		echo "  Add it to .env (see .env.example for the default URL)";
		exit 1;
	fi


# ─── Individual Deployment Steps ────────────────────────────

.PHONY: ingress-install
ingress-install: ## Install NGINX Ingress Controller (idempotent)
	@echo "→ Installing NGINX Ingress Controller..."
	@$(KUBECTL) apply -f \
		https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-$(INGRESS_NGINX_VERSION)/deploy/static/provider/kind/deploy.yaml
	@$(KUBECTL) label node --overwrite $(CLUSTER_NAME)-control-plane ingress-ready=true 2>/dev/null || true
	@echo "✓ Ingress controller installed"

.PHONY: ingress-wait
ingress-wait: ## Wait for ingress controller to be ready
	@echo "→ Waiting for ingress controller..."
	@$(KUBECTL) wait --namespace ingress-nginx \
		--for=condition=available deployment/ingress-nginx-controller \
		--timeout=180s
	@echo "✓ Ingress controller ready"

.PHONY: metrics-install
metrics-install: ## Install metrics-server for HPA autoscaling (idempotent)
	@echo "→ Installing metrics-server..."
	@$(KUBECTL) apply -f \
		https://github.com/kubernetes-sigs/metrics-server/releases/download/$(METRICS_SERVER_VERSION)/components.yaml
	@if $(KUBECTL) get deployment metrics-server -n kube-system \
		-o jsonpath='{.spec.template.spec.containers[0].args}' 2>/dev/null \
		| grep -q -- '--kubelet-insecure-tls'; then
		echo "→ metrics-server already has --kubelet-insecure-tls"
	else
		$(KUBECTL) patch deployment metrics-server -n kube-system --type=json \
			-p='[{"op": "add", "path": "/spec/template/spec/containers/0/args/-", "value": "--kubelet-insecure-tls"}]'
	fi
	@echo "✓ metrics-server installed"

.PHONY: metrics-wait
metrics-wait: ## Wait for metrics-server to be ready
	@echo "→ Waiting for metrics-server..."
	@$(KUBECTL) wait --namespace kube-system \
		--for=condition=available deployment/metrics-server \
		--timeout=180s
	@echo "✓ metrics-server ready"

.PHONY: storage-provisioner-install
storage-provisioner-install: ## Install local-path StorageClass (dynamic provisioning, idempotent)
	@echo "→ Installing local-path StorageClass..."
	@$(KUBECTL) apply -f \
		https://raw.githubusercontent.com/rancher/local-path-provisioner/$(LOCAL_PATH_PROVISIONER_VERSION)/deploy/local-path-storage.yaml
	@echo "✓ local-path StorageClass installed"

.PHONY: storage-provisioner-wait
storage-provisioner-wait: ## Wait for local-path-provisioner to be ready
	@echo "→ Waiting for local-path-provisioner..."
	@if $(KUBECTL) wait --namespace local-path-storage \
		--for=condition=available deployment/local-path-provisioner \
		--timeout=120s 2>/dev/null; then
		echo "✓ local-path-provisioner ready";
	else
		echo "  ⚠ local-path-provisioner not found (may use different namespace)";
	fi

.PHONY: tls-generate
tls-generate: namespace-ensure ## Generate TLS certificate for $(DOMAIN) (idempotent)
	@if $(KUBECTL) get secret nars-tls -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		echo "→ TLS secret 'nars-tls' already exists"
	else
		echo "→ Generating TLS certificate for $(DOMAIN)..."
		TLS_TMPDIR=$$(mktemp -d);
		trap 'rm -rf "$$TLS_TMPDIR"' EXIT;
		CERT_FILE=$$TLS_TMPDIR/tls.crt;
		KEY_FILE=$$TLS_TMPDIR/tls.key;
		mkcert -cert-file "$$CERT_FILE" -key-file "$$KEY_FILE" "$(DOMAIN)"
		CERT_B64=$$(base64 -w0 < "$$CERT_FILE")
		KEY_B64=$$(base64 -w0 < "$$KEY_FILE")
		printf 'apiVersion: v1\nkind: Secret\nmetadata:\n  name: nars-tls\n  namespace: %s\ntype: kubernetes.io/tls\ndata:\n  tls.crt: %s\n  tls.key: %s\n' \
			"$(NAMESPACE)" "$$CERT_B64" "$$KEY_B64" \
		| $(KUBECTL) apply -f -
		echo "✓ TLS secret created"
	fi

.PHONY: ca-generate
ca-generate: ## Generate a self-signed CA certificate for mTLS (idempotent)
	@if [ -f "$(K8S_DIR)/certs/ca.crt" ]; then
		echo "→ CA cert already exists at $(K8S_DIR)/certs/ca.crt"
	else
		echo "→ Generating self-signed CA certificate..."
		mkdir -p "$(K8S_DIR)/certs"
		openssl ecparam -genkey -name prime256v1 -noout -out "$(K8S_DIR)/certs/ca.key"
		openssl req -x509 -new -nodes -key "$(K8S_DIR)/certs/ca.key" \
			-sha256 -days 3650 \
			-subj "/CN=nars-mtls-ca/O=NARS" \
			-out "$(K8S_DIR)/certs/ca.crt"
		chmod 600 "$(K8S_DIR)/certs/ca.key"
		echo "✓ CA cert generated:"
		echo "    cert: $(K8S_DIR)/certs/ca.crt"
		echo "    key:  $(K8S_DIR)/certs/ca.key"
		echo ""
		echo "  Issue client certs with:"
		echo "    openssl req -new -nodes -out client.csr \\"
		echo "      -subj '/CN=client-name/O=NARS'"
		echo "    openssl x509 -req -in client.csr \\"
		echo "      -CA $(K8S_DIR)/certs/ca.crt \\"
		echo "      -CAkey $(K8S_DIR)/certs/ca.key \\"
		echo "      -CAcreateserial -out client.crt -days 365 -sha256"
		echo "    rm client.csr"
	fi

.PHONY: ca-secret
ca-secret: ca-generate namespace-ensure ## Create mTLS CA secret from k8s/certs/ca.crt (idempotent)
	@if $(KUBECTL) get secret nars-ca -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		echo "→ CA secret 'nars-ca' already exists"
	else
		echo "→ Creating mTLS CA secret..."
		CA_FILE="$(K8S_DIR)/certs/ca.crt"
		CA_B64=$$(base64 -w0 < "$$CA_FILE")
		printf 'apiVersion: v1\nkind: Secret\nmetadata:\n  name: nars-ca\n  namespace: %s\ndata:\n  ca.crt: %s\n' \
			"$(NAMESPACE)" "$$CA_B64" \
		| $(KUBECTL) apply -f -
		echo "✓ CA secret created"
	fi

# Materialized `kubectl kustomize` output shared by the apply gates below.
# Rendering it once here — instead of in each of secrets-validate,
# _check-local-ingresses, and the kustomize-apply recipe — cuts kustomize
# evaluation from up to 3 runs per apply to exactly 1. FORCE regenerates the
# file on every invocation (the overlay depends on live vars + on-disk
# kustomization.yaml, so a stale render must never be consumed). The file lives
# under LOG_DIR so it never pollutes the repo.
KUSTOMIZE_MANIFEST := $(LOG_DIR)/kustomize-output.yaml

.PHONY: FORCE
FORCE:

$(KUSTOMIZE_MANIFEST): FORCE
	@mkdir -p "$(@D)"
	@output=$$($(KUBECTL) kustomize "$(K8S_OVERLAY_DIR)" 2>&1) || exit_code=$$?
	@if [ "$${exit_code:-0}" -ne 0 ]; then
		echo "✖ ERROR: kustomize failed:"
		echo "$$output" | head -5
		exit 1
	fi
	@if [ -z "$$output" ]; then
		echo "✖ ERROR: kustomize produced empty output"
		exit 1
	fi
	@printf '%s\n' "$$output" > "$@"
	@chmod 600 "$@"

.PHONY: secrets-validate
secrets-validate: $(KUSTOMIZE_MANIFEST) ## Fail if kustomize output contains placeholder values (REPLACE_ME)
	@echo "→ Validating kustomize overlay (${K8S_OVERLAY_DIR}) for placeholder values..."
	@if grep -q "REPLACE_ME" "$(KUSTOMIZE_MANIFEST)"; then
		echo "✖ ERROR: kustomize output contains REPLACE_ME placeholder values!"
		echo "  Hint: for production, edit $(K8S_OVERLAY_DIR)/patches/health-ingress.yaml"
		echo "  and replace REPLACE_ME_POD_CIDR/REPLACE_ME_SVC_CIDR with your cluster's real CIDRs,"
		echo "  then run 'make secrets-apply' again."
		echo ""
		grep -n "REPLACE_ME" "$(KUSTOMIZE_MANIFEST)"
		exit 1
	fi
	@echo "✓ No placeholder values found"

.PHONY: secrets-apply
secrets-apply: .env _check-secrets namespace-ensure ## Create nars-secrets and regcred with generated/variable values
# SECURITY: Uses temp files instead of --from-literal to avoid secret
# exposure in `ps aux` / CI logs. Files are cleaned up on exit.
	@echo "→ Creating 'nars-secrets'..."
	@tmpdir=$$(mktemp -d);
	trap 'rm -rf "$$tmpdir" $${TMPDIR_REGCRED:-}' EXIT;
	printf '%s' "$$POSTGRES_PASSWORD" > "$$tmpdir/postgres_password";
	printf '%s' "Host=postgis;Port=5432;Database=$(DB_NAME);Username=postgres;Password=$$POSTGRES_PASSWORD" > "$$tmpdir/ConnectionStrings__DefaultConnection";
	printf '%s' "$$JWT_SECRET" > "$$tmpdir/Jwt__SecretKey";
	printf '%s' "$$GPG_PASSPHRASE" > "$$tmpdir/gpg-passphrase";
	printf '%s' "$$NARS_ADMIN_SIGNUP_TOKEN" > "$$tmpdir/AdminSignup__SignupToken";
	printf '%s' "$$NARS_ROADS_INTERNAL_TOKEN" > "$$tmpdir/Segmentation__InternalToken";
	$(KUBECTL) create secret generic nars-secrets -n "$(NAMESPACE)" \
		--from-file=postgres_password="$$tmpdir/postgres_password" \
		--from-file=ConnectionStrings__DefaultConnection="$$tmpdir/ConnectionStrings__DefaultConnection" \
		--from-file=Jwt__SecretKey="$$tmpdir/Jwt__SecretKey" \
		--from-file=gpg-passphrase="$$tmpdir/gpg-passphrase" \
		--from-file=AdminSignup__SignupToken="$$tmpdir/AdminSignup__SignupToken" \
		--from-file=Segmentation__InternalToken="$$tmpdir/Segmentation__InternalToken" \
		--dry-run=client -o yaml \
	| $(KUBECTL) apply -f -
	@echo "✓ nars-secrets created"

	@echo "→ Creating 'nars-roads-secrets' (shared internal token + weights URL)..."
	printf '%s' "$$NARS_ROADS_INTERNAL_TOKEN" > "$$tmpdir/internal-token";
	printf '%s' "$$NARS_ROADS_WEIGHTS_URL" > "$$tmpdir/weights-url";
	$(KUBECTL) create secret generic nars-roads-secrets -n "$(NAMESPACE)" \
		--from-file=internal-token="$$tmpdir/internal-token" \
		--from-file=weights-url="$$tmpdir/weights-url" \
		--dry-run=client -o yaml \
	| $(KUBECTL) apply -f -
	@echo "✓ nars-roads-secrets created"

	@if [ -n "$(DOCKER_TOKEN)" ]; then
		echo "→ Creating 'regcred'...";
		TMPDIR_REGCRED=$$(mktemp -d);
		printf '%s' "$$DOCKER_TOKEN" > "$$TMPDIR_REGCRED/docker_password";
		printf '%s' "$$DOCKER_USERNAME" > "$$TMPDIR_REGCRED/docker_username";
		$(KUBECTL) create secret docker-registry regcred -n "$(NAMESPACE)" \
			--docker-server=https://index.docker.io/v1/ \
			--docker-username-file="$$TMPDIR_REGCRED/docker_username" \
			--docker-password-file="$$TMPDIR_REGCRED/docker_password" \
			--dry-run=client -o yaml \
		| $(KUBECTL) apply -f -;
		echo "✓ regcred created";
	else
		echo "→ Skipping regcred (DOCKER_TOKEN not set — using locally loaded images)";
	fi

.PHONY: kustomize-set-image-tag
kustomize-set-image-tag: _check-tag-syntax ## Persistently pin image tags in kustomization.yaml (manual; kustomize-apply rewrites tags per-run)
	@if [ -z "$(IMAGE_TAG)" ]; then
		echo "✖ IMAGE_TAG is empty — specify a tag (e.g. IMAGE_TAG=abc1234)";
		exit 1;
	fi
	@if echo $(IMAGE_TAG_Q) | grep -qi "^latest$$"; then
		echo "  ⚠ IMAGE_TAG=$(IMAGE_TAG) is 'latest' — not pinning. Set IMAGE_TAG=<commit-sha> for reproducible deployments.";
	else
		if ! command -v kustomize >/dev/null 2>&1; then
			echo "✖ standalone 'kustomize' binary is not installed (needed for 'edit set image').";
			echo "  Use 'make kustomize-apply IMAGE_TAG=$(IMAGE_TAG)' instead — it applies the pin without modifying files.";
			exit 1;
		fi;
		echo "→ Pinning kustomize image tags to $(IMAGE_TAG)...";
		(cd "$(K8S_DIR)" && \
		for img in $(REGISTRY_IMAGES); do \
			kustomize edit set image "$(DOCKER_ORG)/$$img=$(DOCKER_ORG)/$$img":$(IMAGE_TAG_Q); \
		done);
		echo "✓ Image tags pinned to $(IMAGE_TAG)";
	fi

.PHONY: kustomize-apply
kustomize-apply: secrets-validate _check-pinned-tag _check-local-ingresses ## Apply k8s manifests via kustomize (pin tags with IMAGE_TAG=<sha>)
	$(SUBMAKE) postgis-pv-fix
	@echo "→ Applying kustomization (images: $(DOCKER_ORG)/*:"$(IMAGE_TAG_Q)")..."
	# Tag rewriting lives in nars-infra/scripts/kustomize-tag-rewrite.awk
	# (documented + diff-tested against the former inline awk program).
	# Reads the shared render (see $(KUSTOMIZE_MANIFEST) above) instead of
	# re-running `kubectl kustomize`, which already ran in the gates above.
	@awk -v org="$(DOCKER_ORG)" -v tag=$(IMAGE_TAG_Q) -v images="$(REGISTRY_IMAGES)" \
		-f "$(SCRIPTS_DIR)/kustomize-tag-rewrite.awk" < "$(KUSTOMIZE_MANIFEST)" \
	| $(KUBECTL) apply -f -
	@echo "✓ Kustomization applied"

	@echo "→ Waiting for postgis..."
	@if $(KUBECTL) get deployment postgis -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
		$(KUBECTL) wait --namespace "$(NAMESPACE)" \
			--for=condition=Available deployment/postgis --timeout=240s
		$(SUBMAKE) postgis-password-sync
		$(SUBMAKE) postgis-migration-baseline
		$(SUBMAKE) db-migrate-nars
	else
		echo "  ⚠ postgis deployment not found — skipping password sync, baseline, and migrations";
	fi

	@echo "→ Waiting for app deployments..."
	@for deploy in $(filter-out postgis,$(SCALABLE_DEPLOYS)); do
		if $(KUBECTL) get deployment "$$deploy" -n "$(NAMESPACE)" 2>/dev/null >/dev/null; then
			if ! $(KUBECTL) wait --namespace "$(NAMESPACE)" \
				--for=condition=Available "deployment/$$deploy" --timeout=240s; then
				echo "✖ Deployment '$$deploy' did not become Available in time."
				echo "→ describe deployment/$$deploy"
				$(KUBECTL) describe deployment "$$deploy" -n "$(NAMESPACE)" || true
				echo "→ pods for $$deploy"
				$(KUBECTL) get pods -n "$(NAMESPACE)" -l "app.kubernetes.io/name=$$deploy" -o wide || true
				for pod in $$($(KUBECTL) get pods -n "$(NAMESPACE)" -l "app.kubernetes.io/name=$$deploy" -o name); do
					echo "→ logs $$pod (current)"
					$(KUBECTL) logs -n "$(NAMESPACE)" $$pod --tail=120 || true
					echo "→ logs $$pod (previous)"
					$(KUBECTL) logs -n "$(NAMESPACE)" $$pod --previous --tail=120 || true
				done
				exit 1
			fi
		fi
	done
	@echo "✓ All deployments ready"
