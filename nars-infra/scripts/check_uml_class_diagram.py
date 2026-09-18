"""Check docs/uml/nars-class-diagram.md against the nars-api source tree.

The mermaid render gate (make docs-lint-uml) only proves the diagram parses;
it cannot notice a type or member being deleted or renamed in code. This
script closes that gap: every ``class`` and every listed member in the
backend class diagram must resolve to a real type/member in nars-api.

Only the backend class diagram is covered here. The frontend component diagram
has its own equivalent guard in check_uml_vite_component_diagram.py, because
its classes resolve differently (components, stores, composables, map modules).
The sequence diagrams describe HTTP/dependency flows, which have no trivial
source-level index, so they stay under render-only enforcement.

Usage:
    python3 nars-infra/scripts/check_uml_class_diagram.py
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DIAGRAM = REPO_ROOT / "docs/uml/nars-class-diagram.md"
API_DIR = REPO_ROOT / "nars-api"

# Framework types referenced by the diagram but not declared in nars-api
# (ASP.NET Core MVC base classes).
ALLOWLIST_TYPES = {"ControllerBase"}

TYPE_DECL_RE = re.compile(r"\b(?:class|interface|record|struct)\s+([A-Z]\w*)")
_CLASS_RE = re.compile(r"^\s*class\s+(\w+)")
_VISIBILITY_RE = re.compile(r"^\s*([+#~-])")


def strip_non_code(text: str) -> str:
    """Blank out comments and quoted literals, leaving code identifiers only.

    The naive drift guard searched raw file text for \\bname\\b, so a toolbar
    member that had been deleted still passed if its name survived inside a
    comment, an XML doc-declared string, or a string literal (e.g. a README.md
    snippet or a 'TODO: remove Foo' comment). This keeps identifier searches
    honest: only a real token in code counts as the member existing.
    """
    out = list(text)
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            while i < n and text[i] != "\n":
                out[i] = " "
                i += 1
        elif c == "/" and nxt == "*":
            out[i] = out[i + 1] = " "
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                if text[i] != "\n":
                    out[i] = " "
                i += 1
            if i + 1 < n:
                out[i] = out[i + 1] = " "
                i += 2
        elif c == '"':
            verbatim = i > 0 and text[i - 1] == "@"
            out[i] = " "
            i += 1
            while i < n:
                if text[i] == '"':
                    if verbatim and i + 1 < n and text[i + 1] == '"':
                        out[i] = out[i + 1] = " "
                        i += 2
                        continue
                    out[i] = " "
                    i += 1
                    break
                if not verbatim and text[i] == "\\" and i + 1 < n:
                    out[i] = out[i + 1] = " "
                    i += 2
                    continue
                out[i] = " "
                i += 1
        elif c == "'":
            out[i] = " "
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    out[i] = out[i + 1] = " "
                    i += 2
                    continue
                if text[i] == "'":
                    out[i] = " "
                    i += 1
                    break
                out[i] = " "
                i += 1
        else:
            i += 1
    return "".join(out)


@dataclass
class DiagramClass:
    name: str
    members: list[str] = field(default_factory=list)


def extract_diagram() -> list[DiagramClass]:
    """Parse the mermaid classDiagram block into classes and their members."""
    text = DIAGRAM.read_text(encoding="utf-8")
    block_start = text.find("```mermaid")
    if block_start == -1:
        sys.exit("✖ No ```mermaid block found in nars-class-diagram.md")
    block_start += len("```mermaid\n")
    block_end = text.find("```", block_start)
    if block_end == -1:
        sys.exit("✖ Unclosed ```mermaid block in nars-class-diagram.md")
    block = text[block_start:block_end]

    classes: list[DiagramClass] = []
    current: DiagramClass | None = None
    for line in block.splitlines():
        class_match = _CLASS_RE.match(line)
        if class_match:
            current = DiagramClass(name=class_match.group(1))
            classes.append(current)
            continue
        if current is None or not line.strip():
            continue
        member_match = _VISIBILITY_RE.match(line)
        if not member_match:
            continue
        rest = line[member_match.end() :].strip()
        if "(" in rest:
            name = rest[: rest.index("(")].strip()
        else:
            name = rest.split()[-1]
        current.members.append(name)
    return classes


def index_types(files: list[Path]) -> dict[str, list[Path]]:
    """Map type name -> files declaring it."""
    index: dict[str, list[Path]] = {}
    for file in files:
        text = strip_non_code(file.read_text(encoding="utf-8", errors="replace"))
        for match in TYPE_DECL_RE.finditer(text):
            index.setdefault(match.group(1), []).append(file)
    return index


def member_visible(files: list[Path], name: str) -> bool:
    """True if ``name`` appears as a code identifier in any of ``files``.

    Comments and string literals are stripped first, so a deleted member whose
    name survives only in prose (a 'TODO: remove Foo' comment, an XML-doc
    snippet, a log message) no longer counts as present.
    """
    pattern = re.compile(rf"\b{re.escape(name)}\b")
    for file in files:
        text = strip_non_code(file.read_text(encoding="utf-8", errors="replace"))
        if pattern.search(text):
            return True
    return False


def main() -> None:
    if not DIAGRAM.is_file():
        sys.exit(f"✖ Diagram not found: {DIAGRAM}")
    files = [
        f
        for f in API_DIR.rglob("*.cs")
        if "obj" not in f.parts and "bin" not in f.parts
    ]
    if not files:
        sys.exit(f"✖ No C# files found under {API_DIR}")
    type_files = index_types(files)

    errors: list[str] = []
    for class_entry in extract_diagram():
        name = class_entry.name
        if name in ALLOWLIST_TYPES:
            continue
        candidates = type_files.get(name, [])
        if name.startswith("I"):
            candidates = candidates + type_files.get(name[1:], [])
        if not candidates:
            errors.append(f"  type '{name}' is not declared in nars-api")
            continue
        for member in class_entry.members:
            if not member_visible(candidates, member):
                errors.append(
                    f"  member '{name}.{member}' not found in its source files"
                )

    if errors:
        print("✖ nars-class-diagram.md drifted from nars-api source:")
        print("\n".join(errors))
        print("  Regenerate the diagram or fix the listed entries.")
        sys.exit(1)
    print("✓ nars-class-diagram.md matches nars-api source")


if __name__ == "__main__":
    main()
