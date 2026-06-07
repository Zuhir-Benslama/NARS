# Docker images for Kubernetes manifests

These Dockerfiles are separate from app code and align with manifests under `k8s/`.

## API image
Build:
```bash
docker build -f Docker/Dockerfile.nars-api -t zuhirbenslama/nars-api:latest .
```
Push:
```bash
docker push zuhirbenslama/nars-api:latest
```
This matches `k8s/app-deployment.yaml`.  
Images are also built and pushed automatically via CI on pushes to `main`/`develop`.

## PostGIS image
Build:
```bash
docker build -f Docker/Dockerfile.nars-postgis -t zuhirbenslama/nars-postgis:latest .
```
Push:
```bash
docker push zuhirbenslama/nars-postgis:latest
```
This matches `k8s/postgres.yaml`. On first startup the schema from `docs/nars_db.sql` is automatically loaded into the `nars_db` database. The database schema is also managed by EF Core migrations — the init script provides a baseline for fresh deployments.

## Kubernetes pull secret
To pull images from your private Docker Hub repo, create a `regcred` secret in the `nars` namespace:
```bash
kubectl create secret docker-registry regcred \
  --docker-server=https://index.docker.io/v1/ \
  --docker-username=zuhirbenslama \
  --docker-password=<your-token-or-password> \
  --namespace=nars
```
