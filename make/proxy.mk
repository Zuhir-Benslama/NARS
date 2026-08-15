# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: kind<->host port proxying.


PROXY_CONTAINER ?= kind-proxy

.PHONY: port-forward-start
port-forward-start: ## Start kubectl port-forward inside the kind container (background)
	@echo "→ Starting port-forward inside kind container..."
	@docker exec $(CLUSTER_NAME)-control-plane sh -c 'pkill -f "port-forward.*ingress-nginx" 2>/dev/null; true' 2>/dev/null || true
	@sleep 0.5
	@docker exec -d $(CLUSTER_NAME)-control-plane kubectl port-forward --address 0.0.0.0 \
		-n ingress-nginx service/ingress-nginx-controller $(APP_PORT):80 > /dev/null 2>&1
	@docker exec -d $(CLUSTER_NAME)-control-plane kubectl port-forward --address 0.0.0.0 \
		-n ingress-nginx service/ingress-nginx-controller $(APP_TLS_PORT):443 > /dev/null 2>&1
	@sleep 2
	@echo "✓ Port-forward started inside kind container"

.PHONY: port-forward-stop
port-forward-stop: ## Stop port-forward inside the kind container
	@docker exec $(CLUSTER_NAME)-control-plane sh -c 'pkill -f "port-forward.*ingress-nginx" 2>/dev/null; true' 2>/dev/null || true
	@echo "✓ Port-forward stopped"

.PHONY: proxy-up
proxy-up: port-forward-start ## Start Docker socat bridge: host:$(APP_PORT) → kind:$(APP_PORT)
	@echo "→ Setting up socat bridge container..."
	@docker rm -f "$(PROXY_CONTAINER)" 2>/dev/null || true
	@sleep 0.5
	@docker run -d --name "$(PROXY_CONTAINER)" --rm \
		-p 0.0.0.0:$(APP_PORT):$(APP_PORT) \
		-p 0.0.0.0:$(APP_TLS_PORT):$(APP_TLS_PORT) \
		--network kind \
		--entrypoint sh \
		alpine/socat \
		-c "socat tcp-l:$(APP_PORT),fork,reuseaddr tcp:$(CLUSTER_NAME)-control-plane:$(APP_PORT) & socat tcp-l:$(APP_TLS_PORT),fork,reuseaddr tcp:$(CLUSTER_NAME)-control-plane:$(APP_TLS_PORT) & wait -n" > /dev/null
	@echo "→ Waiting for proxy to be ready..."
	@for i in $$(seq 1 15); do
		running=$$(docker inspect -f '{{.State.Running}}' "$(PROXY_CONTAINER)" 2>/dev/null || echo "false");
		if [ "$$running" != "true" ]; then
			echo "✖ socat container '$(PROXY_CONTAINER)' exited unexpectedly";
			docker logs "$(PROXY_CONTAINER)" 2>&1 | tail -5;
			exit 1;
		fi;
		status=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 2 http://localhost:$(APP_PORT)/ 2>/dev/null || echo "000");
		if [ "$$status" != "000" ]; then break; fi;
		sleep 1;
	done;
	status=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 2 http://localhost:$(APP_PORT)/ 2>/dev/null || echo "000");
	if [ "$$status" = "200" ] || [ "$$status" = "302" ]; then
		echo "✓ Proxy ready ($$status)";
	else
		echo "⚠ Proxy may not be reachable (status: $$status — check port-forward or rootless Docker networking)";
	fi
	@echo ""
	@echo "✓ App accessible at http://localhost:$(APP_PORT)/"
	@echo "  Health:      http://localhost:$(APP_PORT)/api/health"
	@echo "  Mobile app:  make adb-reverse    (if connected via USB)"
	@echo "  Smoke test:  make smoke-test"
	@echo "  Stop proxy:  make proxy-down"

.PHONY: proxy-down
proxy-down: port-forward-stop ## Stop the socat bridge and port-forward
	@echo "→ Stopping socat bridge..."
	@docker rm -f "$(PROXY_CONTAINER)" 2>/dev/null || true
	@echo "✓ Proxy stopped"

.PHONY: adb-reverse
adb-reverse: ## Forward phone:$(APP_PORT) → host:$(APP_PORT) via USB (for mobile dev)
	@echo "→ Setting up adb reverse proxy..."
	@adb reverse tcp:$(APP_PORT) tcp:$(APP_PORT) 2>&1
	@echo "✓ Phone can now reach the API at http://localhost:$(APP_PORT)/"
	@echo "  (Lasts while USB is connected; re-run after USB disconnect/reconnect)"

.PHONY: proxy-status
proxy-status: ## Show proxy status
	@echo "=== Port-forward (kind container) ==="
	@docker exec $(CLUSTER_NAME)-control-plane ss -tlnp 2>/dev/null | grep -E '$(APP_PORT)|$(APP_TLS_PORT)' || echo "  NOT RUNNING"
	@echo ""
	@echo "=== socat bridge container ==="
	@docker ps --filter name=$(PROXY_CONTAINER) --format '  {{.ID}} {{.Status}} {{.Image}}' 2>/dev/null || echo "  NOT RUNNING"
	@echo ""
	@echo "=== App health ==="
	@curl -s -o /dev/null -w "  HTTP %{http_code}\n" --connect-timeout 3 http://localhost:$(APP_PORT)/ 2>/dev/null || echo "  UNREACHABLE"
