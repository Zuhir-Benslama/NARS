#!/usr/bin/env python3
"""Rebuild guard for nars images (make/images.mk images-build, req: build only
when modified). Exits 0 when a rebuild is needed (first build, or the content
hash of the image's Dockerfile + every source glob it COPY-depends on changed),
1 when the stamped hash is unchanged (identical sources → skip the docker build,
which images.mk turns into "skip" output instead of re-running docker build).

Usage: image-hash-guard.py <stamp-file> <glob...>

Stamps live under $(IMAGES_HASH_DIR)/ (root FS, next to the kind staging TMPDIR,
NOT the /tmp tmpfs) so they survive reboots and are never reclaimed — same
durability rule as the kind staging dir.

The glob set per image is duplicated from the dorny/paths-filter filters in
.github/workflows/docker.yml. THE TWO MUST STAY IN LOCKSTEP: they encode the one
canonical "what counts as a source change" per image 🔗
(CI paths-filter ↔ local make guard; drift here = CI and local disagree on
"modified", so a rebuild could be skipped by one but not the other).
"""
import glob
import hashlib
import os
import subprocess
import sys


def git_ignored(paths):
    """Return the subset of `paths` that git ignores.

    Mirrors the CI paths-filter exactly: dorny/paths-filter diffs TRACKED files
    only, so gitignored build artifacts (bin/, obj/, dist/, node_modules/,
    TestResults/) must not flip the local stamp either — otherwise every
    dotnet build / npm ci would re-trigger an image rebuild locally while CI
    stays green. Returns an empty set when git is unavailable or the cwd is
    not a repository, which is the safe direction: hash everything.
    """
    if not paths:
        return set()
    try:
        proc = subprocess.run(
            ["git", "check-ignore", "--stdin"],
            input="".join(p + "\n" for p in paths),
            text=True,
            capture_output=True,
            timeout=10,
        )
    except (OSError, subprocess.SubprocessError):
        return set()
    if proc.returncode not in (0, 1):  # 128 → "not a git repository"
        return set()
    if proc.returncode == 0:
        return set(proc.stdout.splitlines())
    return set()


def digest_of(patterns):
    h = hashlib.sha256()
    for pat in patterns:
        matches = sorted(
            p for p in glob.glob(pat, recursive=True) if os.path.isfile(p)
        )
        ignored = git_ignored(matches)
        for path in matches:
            if path in ignored:
                continue
            with open(path, "rb") as fh:
                h.update(fh.read())
            h.update(b"\x00")
    return h.hexdigest()


def main():
    stamp, *patterns = sys.argv[1:]
    want = digest_of(patterns)
    if os.path.isfile(stamp):
        try:
            with open(stamp, encoding="ascii") as fh:
                have = fh.read().strip()
            if have == want:
                return 1  # unchanged → skip
        except OSError:
            pass
    os.makedirs(os.path.dirname(stamp) or ".", exist_ok=True)
    with open(stamp, "w", encoding="ascii") as fh:
        fh.write(want + "\n")
    return 0  # changed / first → rebuild


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:  # ctrl-c mid-guard → don't half-write a stamp
        raise SystemExit(130)
