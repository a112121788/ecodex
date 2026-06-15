#!/usr/bin/env python3
"""Regenerate assets/app-icon.png and assets/app-icon.ico from
assets/app-icon.svg.

Run from repo root:

    uv run --with resvg-py --with pillow python scripts/build-app-icon.py

Outputs:
  * assets/app-icon.png  - 512x512 master PNG (RGBA)
  * assets/app-icon.ico  - multi-resolution .ico (16/32/48/64/128/256),
                           each entry is a 32-bit RGBA PNG, matching the
                           previous .ico structure that the WPF project
                           already references.
  * src/ECodeX/Assets/*   - synchronized copies used by ECodeX.csproj.
"""
from __future__ import annotations

import struct
from pathlib import Path

import resvg_py

ROOT = Path(__file__).resolve().parent.parent
SVG_PATH = ROOT / "assets" / "app-icon.svg"
PNG_PATH = ROOT / "assets" / "app-icon.png"
ICO_PATH = ROOT / "assets" / "app-icon.ico"
PROJECT_ASSET_DIR = ROOT / "src" / "ECodeX" / "Assets"
PROJECT_PNG_PATH = PROJECT_ASSET_DIR / "app-icon.png"
PROJECT_ICO_PATH = PROJECT_ASSET_DIR / "app-icon.ico"

PNG_SIZE = 512
ICO_SIZES = (16, 32, 48, 64, 128, 256)


def render_svg(svg_text: str, size: int) -> bytes:
    """Render the SVG to a 32-bit RGBA PNG byte string at the given size."""
    return resvg_py.svg_to_bytes(svg_string=svg_text, width=size, height=size)


def build_ico(svg_text: str, sizes: tuple[int, ...], out_path: Path) -> None:
    """Build a multi-resolution .ico by embedding a separate PNG for each
    size. We hand-roll the ICONDIR + ICONDIRENTRY + PNG layout so each
    entry is rendered from vector (not downsampled from a single bitmap).
    """
    pngs = [render_svg(svg_text, s) for s in sizes]

    header_size = 6 + 16 * len(sizes)
    offset = header_size
    offsets: list[int] = []
    for data in pngs:
        offsets.append(offset)
        offset += len(data)

    buf = bytearray()
    buf += struct.pack("<HHH", 0, 1, len(sizes))  # ICONDIR
    for size, data, off in zip(sizes, pngs, offsets):
        # width/height are 1 byte; 0 means 256.
        dim = size if size < 256 else 0
        buf += struct.pack(
            "<BBBBHHII",
            dim, dim,         # width, height
            0, 0,             # color count, reserved
            1, 32,            # planes, bit count
            len(data),        # bytes in resource
            off,              # image offset
        )
    for data in pngs:
        buf += data

    out_path.write_bytes(bytes(buf))


def main() -> None:
    svg_text = SVG_PATH.read_text(encoding="utf-8")

    png_bytes = render_svg(svg_text, PNG_SIZE)
    PNG_PATH.write_bytes(png_bytes)
    print(f"wrote {PNG_PATH.relative_to(ROOT)}  "
          f"{PNG_SIZE}x{PNG_SIZE}  {len(png_bytes):,} bytes")

    build_ico(svg_text, ICO_SIZES, ICO_PATH)
    for s in ICO_SIZES:
        print(f"  embedded {s}x{s}")
    print(f"wrote {ICO_PATH.relative_to(ROOT)}  "
          f"sizes={list(ICO_SIZES)}  {ICO_PATH.stat().st_size:,} bytes")

    PROJECT_ASSET_DIR.mkdir(parents=True, exist_ok=True)
    PROJECT_PNG_PATH.write_bytes(PNG_PATH.read_bytes())
    PROJECT_ICO_PATH.write_bytes(ICO_PATH.read_bytes())
    print(f"synced {PROJECT_PNG_PATH.relative_to(ROOT)}")
    print(f"synced {PROJECT_ICO_PATH.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
