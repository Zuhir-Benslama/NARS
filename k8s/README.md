# Kubernetes manifests for NARS

This folder is separate from the app source code and can be applied as-is after you update secrets/image.

## 1) Prerequisites

- A Kubernetes cluster (e.g., [kind](https://kind.sigs.k8s.io/))
- NGINX Ingress Controller installed:

```bash
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml
```

- [mkcert](https://github.com/FiloSottile/mkcert) for local HTTPS certificates

## 2) Update placeholders

- `app-deployment.yaml`: set `image` to your registry/image
- `secret.yaml`: set strong values for:
  - `postgres_password`
  - `ConnectionStrings__DefaultConnection`
  - `Jwt__SecretKey`
  - Docker registry credentials (`regcred`)

## 3) Generate TLS certificate

```bash
mkcert -install
mkcert -cert-file /tmp/nars-tls.crt -key-file /tmp/nars-tls.key nars.dz
kubectl create secret tls nars-tls -n nars --cert=/tmp/nars-tls.crt --key=/tmp/nars-tls.key --dry-run=client -o yaml | kubectl apply -f -
```

## 4) Apply

```bash
kubectl apply -k k8s/
```

## 5) Access

Port-forward the ingress controller:

```bash
kubectl port-forward -n ingress-nginx service/ingress-nginx-controller 8080:80 8443:443
```

Then add to `/etc/hosts`:

```
127.0.0.1 nars.dz
```

Access at:

- **HTTP**: `http://nars.dz:8080/`
- **HTTPS**: `https://nars.dz:8443/`
