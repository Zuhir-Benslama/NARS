# Rewrites image tags in `kubectl kustomize` output for local kind deploys.
#
# Usage (from make kustomize-apply in make/deploy.mk):
#   kubectl kustomize <dir> | \
#     awk -v org=<docker org> -v tag=<image tag> \
#         -v images="img1 img2 ..." \
#         -f nars-infra/scripts/kustomize-tag-rewrite.awk | kubectl apply -f -
#
# 1. Every `image:` line matching "<org>/(<images>):<any-tag>" has its
#    existing ":tag" suffix replaced with :<tag>. This lets kustomize-apply
#    pin per-run tags without mutating kustomization.yaml.
# 2. Every `app.kubernetes.io/version:` label is set to <tag> so deployed
#    workloads report the version they run.
# Everything else passes through unchanged.
BEGIN {
  esc = org
  gsub(/\//, "\\/", esc)
  n = split(images, imgs, " ")
  alts = imgs[1]
  for (i = 2; i <= n; i++)
    alts = alts "|" imgs[i]
  pat = "^ *-? *image: " esc "\\/(" alts "):"
}
$0 ~ pat { sub(/:[^ ]*$/, ":" tag) }
/app\.kubernetes\.io\/version:/ { sub(/version:.*$/, "version: \"" tag "\"") }
{ print }
