"""Split a kustomize render into an admission scaffold and the remainder.

Kubernetes `--dry-run=server` does not persist anything, so it cannot admit a
namespaced object into a namespace that does not yet exist (the namespace is a
separate object in the same render). It also skips LimitRange/ResourceQuota
evaluation entirely (those plugins do not run during dry-run). To make the
server-side dry-run meaningful the caller really applies the OBJECTS THAT MUST
EXIST FIRST — the admission scaffold below — and then dry-runs the rest against
them:

  scaffold = Namespace, LimitRange, ResourceQuota, PersistentVolume
  rest     = every other object in the render

Real-applying the scaffold is harmless on the throwaway CI cluster and it is
exactly the same namespaced admission shape production applies. The remainder
is dry-run server-side, so pods never pull images and nothing else persists.

Usage:
  kubectl apply --dry-run=client -o json -f render.yaml > render.json
  split_k8s_render.py render.json scaffold.json rest.json
"""

from __future__ import annotations

import json
import sys

SCAFFOLD_KINDS = {"Namespace", "LimitRange", "ResourceQuota", "PersistentVolume"}


def dump(items: list[dict], path: str) -> None:
    with open(path, "w") as fh:
        json.dump({"apiVersion": "v1", "kind": "List", "items": items}, fh)


def main(argv: list[str]) -> int:
    if len(argv) != 4:
        print(
            "usage: split_k8s_render.py render.json scaffold.json rest.json",
            file=sys.stderr,
        )
        return 2
    with open(argv[1]) as fh:
        docs = json.load(fh)["items"]
    scaffold = [d for d in docs if d["kind"] in SCAFFOLD_KINDS]
    rest = [d for d in docs if d["kind"] not in SCAFFOLD_KINDS]
    dump(scaffold, argv[2])
    dump(rest, argv[3])
    print(f"scaffold={len(scaffold)} kinds={sorted({d['kind'] for d in scaffold})}")
    print(f"rest={len(rest)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
