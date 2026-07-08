# nars-infra — Status

All quality checks pass.
- CI: infra-lint job (shellcheck, hadolint, yamllint) gates docker-build
- Make: kustomize-set-image-tag + auto-pin on deploy
