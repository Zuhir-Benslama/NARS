# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: post-deploy smoke test.


# ─── Smoke Test ────────────────────────────────────────────

SMOKE_BASE_URL ?= http://localhost:$(APP_PORT)

.PHONY: smoke-test
smoke-test: ## Post-deploy smoke test: verify /health, frontend, and API auth
	@echo "→ Running smoke tests against $(SMOKE_BASE_URL)..."
	@echo ""
	@failed=0;
	pass() { echo "  ✓ $$1"; };
	fail() { echo "  ✖ $$1"; failed=$$((failed + 1)); };
	echo "  1. Health endpoint...";
	health=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 5 --max-time 10 "$(SMOKE_BASE_URL)/health" 2>/dev/null || echo "000");
	if [ "$$health" = "200" ]; then
		body=$$(curl -s --connect-timeout 5 --max-time 10 "$(SMOKE_BASE_URL)/health" 2>/dev/null || echo "");
		if echo "$$body" | grep -qE '"status"[[:space:]]*:[[:space:]]*"Healthy"'; then
			pass "/health → 200 Healthy";
		else
			fail "/health → 200 but body unexpected: $$body";
		fi;
	else
		fail "/health → $$health (expected 200)";
	fi;
	echo "  2. Frontend reachability...";
	frontend=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 5 --max-time 10 "$(SMOKE_BASE_URL)/" 2>/dev/null || echo "000");
	if [ "$$frontend" = "200" ]; then
		pass "/ → 200";
	elif [ "$$frontend" = "302" ] || [ "$$frontend" = "301" ]; then
		pass "/ → $$frontend (redirect — SPA serving correctly)";
	else
		fail "/ → $$frontend (expected 200 or redirect)";
	fi;
	echo "  3. API auth endpoint...";
	auth=$$(curl -s -o /dev/null -w "%{http_code}" --connect-timeout 5 --max-time 10 \
		-X POST "$(SMOKE_BASE_URL)/api/signin" \
		-H "Content-Type: application/json" \
		-d '{"username":"nonexistent","password":"bad"}' 2>/dev/null || echo "000");
	if [ "$$auth" = "401" ]; then
		pass "POST /api/signin → 401 (auth endpoint alive, bad creds rejected)";
	else
		fail "POST /api/signin → $$auth (expected 401)";
	fi;
	echo "";
	if [ "$$failed" -eq 0 ]; then
		echo "✓ All smoke tests passed!";
	else
		echo "✖ $$failed smoke test(s) failed";
		exit 1;
	fi
