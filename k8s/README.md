# Kubernetes manifests for NARS

This folder is separate from the app source code and can be applied as-is after you update secrets/image.

## 1) Update placeholders
- `app-deployment.yaml`: set `image: your-registry/nars-api:latest`
- `secret.yaml`: set strong values for:
  - `postgres_password`
  - `ConnectionStrings__DefaultConnection`
  - `Jwt__SecretKey`

## 2) Apply
```bash
kubectl apply -k k8s/
```

## 3) Optional host mapping for ingress
If you use local ingress with host `nars.local`, add an `/etc/hosts` entry to point it to your ingress controller IP.
