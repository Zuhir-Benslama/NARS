#!/usr/bin/env python3
from PIL import Image
from pathlib import Path
import sys

input_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("docs/uml")

for png in sorted(input_dir.glob("*.png")):
    pdf = png.with_suffix(".pdf")
    print(f"Converting {png.name} -> {pdf.name}...")
    img = Image.open(png).convert("RGB")
    img.save(pdf, "PDF", resolution=150)

print("Done.")
