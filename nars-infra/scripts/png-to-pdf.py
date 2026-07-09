#!/usr/bin/env python3
"""Convert PNG files in a directory to PDF."""

import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Error: Pillow is not installed. Run: pip install Pillow", file=sys.stderr)
    sys.exit(1)


def main() -> None:
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print(f"Usage: {sys.argv[0]} <input-dir>", file=sys.stderr)
        print("Converts all PNG files in <input-dir> to PDF.", file=sys.stderr)
        sys.exit(0 if args and args[0] in ("-h", "--help") else 1)

    input_dir = Path(args[0])
    if not input_dir.is_dir():
        print(f"Error: '{input_dir}' is not a directory", file=sys.stderr)
        sys.exit(1)

    pngs = sorted(input_dir.glob("*.png"))
    if not pngs:
        print(f"No PNG files found in '{input_dir}'")
        sys.exit(0)

    for png in pngs:
        pdf = png.with_suffix(".pdf")
        print(f"Converting {png.name} -> {pdf.name}...")
        try:
            img = Image.open(png).convert("RGB")
        except Exception as e:
            print(f"Error: cannot open '{png.name}' — {e}", file=sys.stderr)
            continue
        img.save(pdf, "PDF", resolution=150)

    print("Done.")


if __name__ == "__main__":
    main()
