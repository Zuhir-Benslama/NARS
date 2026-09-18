"""Check that the Docker-init schema and the migrations/ DDL stay in sync.

``scripts/create_nars_db.sql`` (§10, ``ai_draft_features``) is applied when a
fresh PostgreSQL image starts, and ``migrations/0001_create_ai_draft_features.sql``
is applied by ``make db-migrate-nars`` to any cluster. Both are meant to define
the same table on the same databases, so their constraint and index names must
agree.

Why names matter: the guarded DDL uses ``CREATE INDEX IF NOT EXISTS`` and
``CREATE TABLE IF NOT EXISTS ... CONSTRAINT ...``, and PostgreSQL matches those
BY NAME. A constraint or index renamed in one file but not the other silently
creates a second (differently-named) object instead of no-oping — no error, just
database cruft and divergent schemas. This script is the mechanical version of
the hand-written warning in the migration file header.

``ai_draft_features_pkey`` is exempted: ``create_nars_db.sql`` spells the
primary-key constraint out, while the migration relies on PostgreSQL's default
``<table>_pkey`` naming — the two resolve to the same name in practice.

Usage:
    python3 nars-infra/scripts/check_migration_drift.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CREATE_DB_SQL = REPO_ROOT / "nars-infra/scripts/create_nars_db.sql"
MIGRATION_SQL = REPO_ROOT / "nars-infra/migrations/0001_create_ai_draft_features.sql"

# Only names attached to the ai_draft_features unit are compared — the schema
# init file defines the whole database, so its constraint names must be scoped.
_PREFIXES = ("ix_ai_draft_", "chk_ai_draft_", "ai_draft_features_")

_CONSTRAINT_RE = re.compile(r"\bCONSTRAINT\s+([A-Za-z0-9_]+)")
_INDEX_RE = re.compile(
    r"\bCREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z0-9_]+)"
)


def _collect_names(path: Path) -> set[str]:
    names: set[str] = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        for match in _CONSTRAINT_RE.finditer(line):
            names.add(match.group(1))
        for match in _INDEX_RE.finditer(line):
            names.add(match.group(1))
    # Scope to the ai_draft_features unit (filters out every other object in the
    # schema-init file) and drop the auto-named primary key (see module docstring).
    return {
        name
        for name in names
        if name.startswith(_PREFIXES) and not name.endswith("_pkey")
    }


def main() -> int:
    if not CREATE_DB_SQL.is_file():
        print(f"✖ missing {CREATE_DB_SQL}")
        return 1
    if not MIGRATION_SQL.is_file():
        print(f"✖ missing {MIGRATION_SQL}")
        return 1

    create_names = _collect_names(CREATE_DB_SQL)
    migration_names = _collect_names(MIGRATION_SQL)

    if create_names == migration_names:
        print(
            f"✓ create_nars_db.sql and migrations define identical ai_draft "
            f"objects ({len(create_names)} names)"
        )
        return 0

    def _short(path: Path) -> str:
        try:
            return str(path.relative_to(REPO_ROOT))
        except ValueError:
            return str(path)

    print(
        "✖ ai_draft constraint/index names drifted between the Docker-init "
        f"schema ({_short(CREATE_DB_SQL)}) and the migration "
        f"({_short(MIGRATION_SQL)})"
    )
    print(
        "  PostgreSQL's IF NOT EXISTS guards match by name, so divergent names "
        "silently create duplicate objects on a shared database."
    )
    if create_names - migration_names:
        print(f"  only in create_nars_db.sql: {sorted(create_names - migration_names)}")
    if migration_names - create_names:
        print(f"  only in the migration:    {sorted(migration_names - create_names)}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
