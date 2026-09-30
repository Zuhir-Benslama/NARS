# Rewrites image references in kubectl kustomize output from tag form to
# content-addressed (digest) form, for NON-dev deploys run by make
# kustomize-apply (see make/deploy.mk). Runs AFTER kustomize-tag-rewrite.awk
# (which pins the immutable-ish sha tag + version label); this stage dereferences
# that tag to its registry digest so a re-tagged or overwritten tag can never
# silently change what a production deployment runs.
#
# Usage (piped after kustomize-tag-rewrite.awk):
#   awk -v org=<docker org> -v images="img1 img2 ..." \
#       -v digest_file=<file> \
#       -f nars-infra/scripts/kustomize-digest-rewrite.awk
#
# digest_file lines:  "<image> <sha256:...>"  (one per image; no leading org)
#
# 1. Every `image:` line matching "<org>/(<images>):<tag>" gets its ":<tag>"
#    suffix replaced with "@<digest>" from the table. Lines whose image name is
#    not in the table are skipped, and references already in "@sha256:" digest
#    form are left untouched (the pattern requires a bare ":<tag>").
# 2. FAILS CLOSED (exit 1): an image that matches a table member but has NO
#    digest entry aborts the run — a non-dev deploy must never fall back to a
#    mutable tag. The caller (make) gates kubectl on this awk's exit status, so
#    nothing is applied.
BEGIN {
  esc = org
  gsub(/\//, "\\/", esc)
  n = split(images, imgs, " ")
  alts = imgs[1]
  for (i = 2; i <= n; i++)
    alts = alts "|" imgs[i]
  pat = "^ *-? *image: " esc "\\/(" alts "):"
  while ((getline line < digest_file) > 0) {
    split(line, f, " ")
    digests[f[1]] = f[2]
  }
  close(digest_file)
}
$0 ~ pat {
  rest = $0
  sub(/^ *-? *image: +/, "", rest)
  sub(/[ @].*$/, "", rest)
  sub(/^.*\//, "", rest)
  sub(/:.*$/, "", rest)
  if (!(rest in digests)) {
    print "✖ kustomize-digest-rewrite: no registry digest for " rest " (missing from " digest_file ")" > "/dev/stderr"
    missing[rest] = 1
  } else {
    sub(/:[^ ]*$/, "@" digests[rest])
  }
}
{ print }
END {
  for (m in missing) nerr++
  if (nerr > 0) {
    print "✖ kustomize-digest-rewrite: " nerr " image(s) had no digest — refusing to apply (no mutable-tag fallback)." > "/dev/stderr"
    exit 1
  }
}