# Kubernetes manifests for NARS

This folder contains the Kubernetes manifests and a Makefile at the project root
that automates cluster creation, deployment, and teardown.

## Quick Start

```bash
# Full bootstrap (creates kind cluster, installs ingress, deploys everything)
make cluster-up

# Port-forward ingress controller to localhost
make cluster-port-forward

# Add to /etc/hosts
echo '127.0.0.1 nars.dz' | sudo tee -a /etc/hosts

# Visit
#   http://nars.dz:8080/       — Frontend
#   http://nars.dz:8080/health — API health
```

## Essential Commands

### Cluster Lifecycle

| Command | Description |
|---------|-------------|
| `make cluster-up` | Full bootstrap — create cluster, deploy everything |
| `make cluster-down` | Delete cluster (postgis data preserved) |
| `make cluster-rebuild` | Delete and recreate the cluster |
| `make cluster-clean` | Delete cluster **and wipe all postgis data** (irreversible) |
| `make cluster-status` | Show all cluster resources |

### Stop / Resume (keep cluster, free resources)

| Command | Description |
|---------|-------------|
| `make cluster-stop` | Scale all deployments to 0 (pods removed, data intact) |
| `make cluster-start` | Scale deployments back to original replica count |
| `make cluster-restart` | Stop → Start |

### Port-Forwarding

| Command | Description |
|---------|-------------|
| `make cluster-port-forward` | Forward ingress controller to `localhost:8080` (background) |

### Database

| Command | Description |
|---------|-------------|
| `make db-backup` | Dump database to `data/nars/postgis/backups/` |
| `make db-restore FILE=...` | Restore from a backup file |
| `make db-shell` | Open interactive `psql` inside the postgis pod |

### Docker Images

| Command | Description |
|---------|-------------|
| `make images-build` | Build all Docker images |
| `make images-push` | Push images to Docker Hub |
| `make images-load` | Load local images into the kind cluster |

### Logs

| Command | Description |
|---------|-------------|
| `make cluster-logs` | Tail logs from all pods |
| `make cluster-status` | Show resources and endpoints |

## Prerequisites

- [kind](https://kind.sigs.k8s.io/)
- [kubectl](https://kubernetes.io/docs/tasks/tools/)
- [mkcert](https://github.com/FiloSottile/mkcert) (for local TLS certificates)
- [docker](https://docs.docker.com/get-docker/)

Verify with:

```bash
make prerequisites
```

## Configuration

Copy `.env.example` to `.env` and customize:

```bash
cp .env.example .env
```

Key variables (all optional):

| Variable | Default | Description |
|----------|---------|-------------|
| `CLUSTER_NAME` | `nars` | Kind cluster name |
| `DOMAIN` | `nars.dz` | TLS certificate domain |
| `POSTGRES_DATA_DIR` | `data/nars/postgis` | Host path for postgis data (hostPath mount) |
| `DOCKER_TOKEN` | — | Docker Hub token for `regcred` (optional — uses local images otherwise) |

## Manual Steps (without Makefile)

If you prefer to run the raw commands instead of using the Makefile:

### 1. Create kind cluster

```bash
kind create cluster --name nars --config nars-infra/k8s/kind-config.yaml
```

### 2. Install NGINX Ingress Controller

```bash
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml
```

Wait for it to be ready:

```bash
kubectl wait --namespace ingress-nginx \
  --for=condition=available deployment/ingress-nginx-controller --timeout=180s
```

### 3. Generate TLS certificate

```bash
mkcert -install
mkcert -cert-file /tmp/nars-tls.crt -key-file /tmp/nars-tls.key nars.dz
kubectl create secret tls nars-tls -n nars \
  --cert=/tmp/nars-tls.crt --key=/tmp/nars-tls.key \
  --dry-run=client -o yaml | kubectl apply -f -
```

### 4. Create secrets

```bash
kubectl create secret generic nars-ca -n nars \
  --from-file=ca.crt=nars-infra/k8s/certs/ca.crt

kubectl create secret generic nars-secrets -n nars \
  --from-literal=postgres_password="<your-password>" \
  --from-literal=ConnectionStrings__DefaultConnection="Host=postgis;Port=5432;Database=nars_db;Username=postgres;Password=<your-password>" \
  --from-literal=Jwt__SecretKey="<your-jwt-secret>" \
  --dry-run=client -o yaml | kubectl apply -f -
```

### 5. Apply manifests

```bash
kubectl apply -k nars-infra/k8s/
```

### 6. Access

```bash
kubectl port-forward -n ingress-nginx service/ingress-nginx-controller 8080:80 8443:443
```

Add to `/etc/hosts`:

```
127.0.0.1 nars.dz
```

- **HTTP**: `http://nars.dz:8080/`
- **HTTPS**: `https://nars.dz:8443/`

## Data Persistence

PostGIS data is stored on your host machine at `$(POSTGRES_DATA_DIR)`
(default: `data/nars/postgis/`). This survives `make cluster-down` and
`kind delete cluster`.

Backups are written to `$(POSTGRES_DATA_DIR)/backups/`.
