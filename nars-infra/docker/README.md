# Docker images for Kubernetes manifests

These Dockerfiles are separate from app code and align with manifests under `nars-infra/k8s/`.

## API image
Build:
```bash
docker build -f nars-infra/docker/Dockerfile.nars-api -t zuhirbenslama/nars-api:latest .
```
Push:
```bash
docker push zuhirbenslama/nars-api:latest
```
This matches `nars-infra/k8s/app-deployment.yaml`.  
Images are also built and pushed automatically via CI on pushes to `main`/`develop`.

## PostGIS image
Build:
```bash
docker build -f nars-infra/docker/Dockerfile.nars-postgis -t zuhirbenslama/nars-postgis:latest .
```
Push:
```bash
docker push zuhirbenslama/nars-postgis:latest
```
This matches `nars-infra/k8s/postgis.yaml`. On first startup the schema from `nars-infra/scripts/create_nars_db.sql` is automatically loaded into the `nars_db` database. The database schema is also managed by EF Core migrations — the init script provides a baseline for fresh deployments.

## Kubernetes pull secret
The images are hosted on Docker Hub as public repositories, so no pull secret is
required for standard deployments. If you switch to a private repository, create a
`regcred` secret in the `nars` namespace:
```bash
kubectl create secret docker-registry regcred \
  --docker-server=https://index.docker.io/v1/ \
  --docker-username=zuhirbenslama \
  --docker-password=<your-token-or-password> \
  --namespace=nars
```
Then add `imagePullSecrets: [{ name: regcred }]` to each deployment's `spec.template.spec`.
