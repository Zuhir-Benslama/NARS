#!/usr/bin/env python3
"""Prune old database backups while always keeping the newest N files."""

from __future__ import annotations

import argparse
import re
import sys
from datetime import datetime, timedelta
from pathlib import Path
from zoneinfo import ZoneInfo

TIMEZONE = ZoneInfo("Africa/Algiers")
TIMESTAMP_RE = re.compile(r"(?P<date>\d{8})_(?P<time>\d{6}|\d{4})")
BACKUP_SUFFIXES = (".sql.gz.gpg", ".sql.gz", ".sql")


def parse_backup_timestamp(name: str) -> datetime | None:
    match = TIMESTAMP_RE.search(name)
    if match is None:
        return None

    date_part = match.group("date")
    time_part = match.group("time")
    fmt = "%Y%m%d_%H%M%S" if len(time_part) == 6 else "%Y%m%d_%H%M"
    try:
        return datetime.strptime(f"{date_part}_{time_part}", fmt).replace(
            tzinfo=TIMEZONE
        )
    except ValueError:
        return None


def find_backups(directory: Path) -> list[tuple[Path, datetime]]:
    found: list[tuple[Path, datetime]] = []
    for path in directory.iterdir():
        if path.is_symlink() or not path.is_file():
            continue
        if not path.name.endswith(BACKUP_SUFFIXES):
            continue
        timestamp = parse_backup_timestamp(path.name)
        if timestamp is not None:
            found.append((path, timestamp))
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dir", default="backup", help="Backup directory")
    parser.add_argument(
        "--days", type=int, default=30, help="Delete files older than this"
    )
    parser.add_argument(
        "--keep", type=int, default=5, help="Always keep this many newest files"
    )
    parser.add_argument(
        "--dry-run", action="store_true", help="Print actions without deleting"
    )
    args = parser.parse_args()

    if args.days < 1:
        parser.error("--days must be at least 1")
    if args.keep < 1:
        parser.error("--keep must be at least 1")

    directory = Path(args.dir).expanduser()
    if not directory.is_dir():
        print(f"✖ Not a directory: {directory}", file=sys.stderr)
        return 1

    backups = find_backups(directory)
    protected = {
        path
        for path, _ in sorted(
            backups, key=lambda item: (item[1], item[0].name), reverse=True
        )[: args.keep]
    }
    cutoff = datetime.now(tz=TIMEZONE) - timedelta(days=args.days)

    deleted = 0
    for path, timestamp in sorted(backups, key=lambda item: item[1]):
        if path in protected or timestamp >= cutoff:
            continue
        if args.dry_run:
            print(f"would delete {path}")
        else:
            path.unlink()
            print(f"deleted {path}")
        deleted += 1

    print(
        f"kept {len(backups) - deleted} backups (protected {len(protected)}), pruned {deleted}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
