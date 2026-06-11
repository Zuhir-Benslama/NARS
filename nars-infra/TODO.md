# nars-infra — Code Quality Issues

## Medium Priority

- [ ] **Shell injection risk** (`scripts/create_national_admin.sh:68-76,81-89`): Two calls use `python3 -c "..."` with `${DB_HOST}`, `${DB_PORT}`, `${DB_NAME}`, `${DB_USER}`, `${DB_PASSWORD}` shell-expanded directly into the Python string. A malicious DB_HOST containing `'` or `$(...)` could inject shell commands. Convert to the `<< 'PYEOF'` heredoc + argv pattern used in the rest of the script (lines 156-195, 205-222).

## Low Priority

- [ ] **Password in process listing** (`scripts/create_national_admin.sh:156-158,205-207`): `DB_PASSWORD` is passed as a command-line argument to `python3`, visible in `ps aux`. Could use a named pipe or environment variable instead.

- [ ] **`pip3 install --break-system-packages`** (`scripts/create_national_admin.sh:46`): Violates PEP 668, though it has a graceful fallback to `pip3 install --quiet`.

- [ ] **19 k8s YAML files missing `---` document start**: yamllint warning. Cosmetic but trivially fixable.

- [ ] **7 YAML lines exceed 80 chars** (`secret.yaml:4,14,20,22,30,33`, `app-deployment.yaml:44-45`, `kustomization.yaml:20,28,33`, `kube-prometheus-stack.yaml:2`): yamllint error. Break long lines.

- [ ] **No infra lint targets in Makefile**: Missing `shellcheck`, `hadolint`, and `yamllint` targets for CI. Consider adding a `lint-infra` target.

- [ ] **`Dockerfile.nars-vite` has no `.dockerignore`**: Test/spec files are copied into the build context but don't affect final image. Minor, but best practice to exclude them.
