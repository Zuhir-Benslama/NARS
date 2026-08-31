# Included by the top-level Makefile (GNU make: single instance, shared vars). Target grouping: backend (nars-tests) + roads test suites.


.PHONY: test
test: ## Run all tests
	dotnet test nars-tests/NarsApi.Tests.csproj --no-restore

.PHONY: test-unit
test-unit: ## Run only unit tests (no Postgres container)
	dotnet test nars-tests/NarsApi.Tests.csproj --no-restore --filter "Category!=Service"

.PHONY: test-service
test-service: ## Run only Postgres-backed service tests (requires Docker for Testcontainers)
	@docker info >/dev/null 2>&1 || { echo "✖ Docker daemon is not running (required by Testcontainers)"; exit 1; }
	dotnet test nars-tests/NarsApi.Tests.csproj --filter "Category=Service"

.PHONY: test-coverage
test-coverage: ## Run unit tests with coverage and enforce thresholds (coverlet.msbuild; no Postgres container)
	dotnet test nars-tests/NarsApi.Tests.csproj --no-restore \
		--filter "Category!=Service" \
		/p:CollectCoverage=true \
		/p:CoverletOutputFormat=cobertura \
		/p:CoverletOutput=TestResults/coverage.cobertura.xml

# Extra `docker build` args for the roads test image. Left empty for local
# one-shot builds; CI supplies BuildKit gha cache flags (via env) so the heavy
# torch base layer is warmed across runs. Keeps the Makefile as the single
# owner of the build command while letting CI tune caching.
ROADS_TEST_DOCKER_BUILD_ARGS ?=

.PHONY: roads-test
roads-test: ## Run the segmentation service's Python test suite in a container
	docker build $(ROADS_TEST_DOCKER_BUILD_ARGS) \
		-f "$(DOCKER_DIR)/Dockerfile.nars-roads" \
		--target test \
		-t "$(DOCKER_ORG)/nars-roads:test" nars-roads/
	docker run --rm "$(DOCKER_ORG)/nars-roads:test"
