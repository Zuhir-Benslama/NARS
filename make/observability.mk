# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: observability stack (LGTM + OTel).


# ─── Observability (Grafana LGTM + OTel) ─────────────────────

.PHONY: observability-install
observability-install: .env _check-secrets helm-check helm-repos ## Install LGTM stack + OpenTelemetry Collector
	$(SUBMAKE) observability-namespace
	$(SUBMAKE) observability-prometheus-stack
	$(SUBMAKE) observability-loki
	$(SUBMAKE) observability-tempo
	$(SUBMAKE) observability-otel-collector
	$(SUBMAKE) observability-servicemonitor
	@echo ""
	@echo "✓ Observability stack installed!"
	@echo ""
	@echo "  Port-forward:  make observability-port-forward"
	@echo "  Grafana:       http://localhost:3000 (credentials in local password manager)"
	@echo "  Loki:          http://localhost:3100"
	@echo "  Tempo:         http://localhost:3200"

.PHONY: helm-check
helm-check: ## Check that helm is installed
	@command -v helm >/dev/null 2>&1 || { echo "✖ helm is not installed"; exit 1; }

.PHONY: helm-repos
helm-repos: ## Ensure required Helm chart repos are configured
	@echo "→ Configuring Helm repositories..."
	@helm repo add prometheus-community https://prometheus-community.github.io/helm-charts --force-update
	@helm repo add grafana https://grafana.github.io/helm-charts --force-update
	@helm repo add open-telemetry https://open-telemetry.github.io/opentelemetry-helm-charts --force-update
	@helm repo update
	@echo "✓ Helm repositories ready"

.PHONY: observability-namespace
observability-namespace: ## Ensure observability namespace exists (idempotent)
	@$(KUBECTL) create namespace $(OBSERVABILITY_NAMESPACE) --dry-run=client -o yaml | $(KUBECTL) apply -f -

.PHONY: observability-prometheus-stack
observability-prometheus-stack: .env _check-secrets ## Install Prometheus + Grafana + AlertManager
	@echo "→ Installing kube-prometheus-stack..."
	@tmpdir=$$(mktemp -d);
	trap 'rm -rf "$$tmpdir"' EXIT;
	printf '%s' "$$GRAFANA_PASSWORD" > "$$tmpdir/grafana_password";
	helm upgrade --install prometheus-stack prometheus-community/kube-prometheus-stack \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--version "$(KUBE_PROMETHEUS_STACK_VERSION)" \
		--values $(K8S_DIR)/helm-values/kube-prometheus-stack.yaml \
		--set-file grafana.adminPassword="$$tmpdir/grafana_password" \
		--timeout 10m
	@echo "✓ kube-prometheus-stack installed"

.PHONY: observability-loki
observability-loki: ## Install Loki (logs)
	@echo "→ Installing Loki..."
	@helm upgrade --install loki grafana/loki \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--version "$(LOKI_VERSION)" \
		--set deploymentMode=SingleBinary \
		--set loki.commonConfig.replication_factor=1 \
		--set loki.auth_enabled=false \
		--set loki.storage.type=filesystem \
		--set loki.storage.bucketNames.chunks=loki-chunks \
		--set loki.storage.bucketNames.admin=loki-admin \
		--set loki.useTestSchema=true \
		--set loki.ruler.enabled=false \
		--set singleBinary.replicas=1 \
		--set singleBinary.persistence.volumeClaimsEnabled=false \
		--set singleBinary.resources.requests.cpu=50m \
		--set singleBinary.resources.requests.memory=128Mi \
		--set singleBinary.resources.limits.cpu=200m \
		--set singleBinary.resources.limits.memory=512Mi \
		--set write.replicas=0 \
		--set read.replicas=0 \
		--set backend.replicas=0 \
		--set chunksCache.enabled=false \
		--set resultsCache.enabled=false \
		--set test.enabled=false \
		--set monitoring.lokiCanary.enabled=false \
		--set monitoring.selfMonitoring.enabled=false \
		--timeout 10m

.PHONY: observability-tempo
observability-tempo: ## Install Tempo (traces)
	@echo "→ Installing Tempo..."
	@helm upgrade --install tempo grafana/tempo \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--version "$(TEMPO_VERSION)" \
		--set tempo.replicas=1 \
		--set tempo.resources.requests.cpu=50m \
		--set tempo.resources.requests.memory=128Mi \
		--set tempo.resources.limits.cpu=200m \
		--set tempo.resources.limits.memory=512Mi \
		--set tempo.storage.trace.backend=local \
		--set tempo.storage.trace.local.path=/var/tempo/traces \
		--set tempo.storage.trace.wal.path=/var/tempo/wal \
		--set tempo.ingester.max_block_duration=5m \
		--set tempo.readinessProbe.initialDelaySeconds=60 \
		--set tempo.readinessProbe.failureThreshold=10 \
		--set tempo.livenessProbe.initialDelaySeconds=60 \
		--set tempo.livenessProbe.failureThreshold=10 \
		--set memBallastSizeMbs=128 \
		--set test.enabled=false \
		--timeout 10m

.PHONY: observability-otel-collector
observability-otel-collector: ## Install OpenTelemetry Collector
	@echo "→ Installing OpenTelemetry Collector..."
	@helm upgrade --install otel-collector open-telemetry/opentelemetry-collector \
		--namespace $(OBSERVABILITY_NAMESPACE) \
		--version "$(OTEL_COLLECTOR_VERSION)" \
		--values $(K8S_DIR)/helm-values/opentelemetry-collector.yaml \
		--set image.tag="$(OTEL_COLLECTOR_IMAGE_TAG)" \
		--timeout 10m

.PHONY: observability-servicemonitor
observability-servicemonitor: ## Apply OTel metrics Service + ServiceMonitor (requires prometheus CRDs)
	@echo "→ Applying OTel metrics Service and ServiceMonitor..."
	@$(KUBECTL) apply -f - < $(K8S_DIR)/otel-metrics-service.yaml
	@$(KUBECTL) apply -f - < $(K8S_DIR)/servicemonitor.yaml
	@echo "✓ OTel metrics Service + ServiceMonitor applied"

.PHONY: observability-port-forward
observability-port-forward: ## Port-forward Grafana, Loki, Tempo (background)
	@mkdir -p "$(LOG_DIR)";
	echo "→ Starting observability port-forwards (background)...";
	pkill -f "port-forward.*observability.*grafana" 2>/dev/null || true;
	pkill -f "port-forward.*observability.*loki-gateway" 2>/dev/null || true;
	pkill -f "port-forward.*observability.*tempo" 2>/dev/null || true;
	sleep 0.5;
	nohup $(KUBECTL) port-forward -n $(OBSERVABILITY_NAMESPACE) \
		service/prometheus-stack-grafana 3000:3000 \
		> "$(LOG_DIR)/port-forward-grafana.log" 2>&1 & \
	nohup $(KUBECTL) port-forward -n $(OBSERVABILITY_NAMESPACE) \
		service/loki-gateway 3100:80 \
		> "$(LOG_DIR)/port-forward-loki.log" 2>&1 & \
	nohup $(KUBECTL) port-forward -n $(OBSERVABILITY_NAMESPACE) \
		service/tempo 3200:3200 \
		> "$(LOG_DIR)/port-forward-tempo.log" 2>&1 & \
	echo "→ Waiting for port-forwards to come up...";
	sleep 3;
	ok=1;
	for pf in grafana loki tempo; do \
		log="$(LOG_DIR)/port-forward-$$pf.log"; \
		if grep -q "Forwarding from" "$$log" 2>/dev/null; then \
			echo "  ✓ $$pf forwarding"; \
		else \
			echo "  ⚠ $$pf did not start (log: $$log)"; \
			ok=0; \
		fi; \
	done;
	if [ "$$ok" -ne 1 ]; then \
		echo "✖ One or more port-forwards failed to start — inspect the logs above"; \
		exit 1; \
	fi;
	echo "  Grafana: http://localhost:3000 (run \`make grafana-password\` to retrieve credentials)";
	echo "  Loki:    http://localhost:3100";
	echo "  Tempo:   http://localhost:3200";
	echo "  Logs:    tail -f $(LOG_DIR)/port-forward-{grafana,loki,tempo}.log"

.PHONY: observability-stop
observability-stop: ## Stop observability port-forwards
	@pkill -f "port-forward.*observability" 2>/dev/null || true
	@echo "✓ Port-forwards stopped"

.PHONY: grafana-password
grafana-password: ## Show the generated Grafana admin password (stderr only, non-CI safe)
	@if [ -t 2 ]; then
		# Shell env var ($${...}) not Make expansion ($(...)) so a password
		# containing shell metacharacters cannot break the quoting — same rule
		# documented at postgis-password-sync.
		echo "$${GRAFANA_PASSWORD}" >&2;
	else
		echo "⚠ Refusing to print password to non-tty stderr (use 'make grafana-password' interactively)" >&2;
		exit 1;
	fi
