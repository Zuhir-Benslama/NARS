# NARS TODO

## Deployment robustness

- [ ] Pin `IMAGE_TAG=<commit-sha>` instead of `latest` so a bad rollout is
      rollback-able. `latest` is mutable and the previous image gets garbage-
      collected, leaving no rollback path. `make deploy` already warns about
      this; make the non-dev deploy pipeline require a pinned tag
      (guards exist in `make/images.mk`: `_check-pinned-tag` / `_warn-latest-tag`).

