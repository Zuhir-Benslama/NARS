# Docker images for Kubernetes manifests

These Dockerfiles are separate from app code and align with manifests under `k8s/`.

## API image
Build:
```bash
docker build -f Docker/Dockerfile.nars-api -t your-registry/nars-api:latest .
```
Push:
```bash
docker push your-registry/nars-api:latest
```
This matches `k8s/app-deployment.yaml`.

## Optional Postgres image
Build:
```bash
docker build -f Docker/Dockerfile.postgres -t your-registry/nars-postgres:15 .
```
Push:
```bash
docker push your-registry/nars-postgres:15
```
If you use this custom image, update `image:` in `k8s/postgres.yaml`.
