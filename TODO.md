# NARS Monorepo — Status

## All Projects Clean
- **nars-api** — 269 tests, lint clean, build clean
- **nars-web** — 801 tests, 59.94% line coverage, lint clean
- **nars-infra** — CI gated (infra-lint), make targets fixed

## What Was Done
- All unit tests written for src/map/ (edit, roads, snapping, core, features, context-menu)
- All Vue SFCs tested (14 component test files)
- All Pinia stores tested
- CI infra-lint job wired in
- make kustomize-set-image-tag + auto-pin
