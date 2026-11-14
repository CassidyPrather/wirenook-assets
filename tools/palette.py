#!/usr/bin/env python3
"""Bakes a palette description into the swatch texture models are UV-mapped onto.

    tools/palette.py Packages/net.wirenook.assets/Textures/wirenook-palette.toml

Flat-shaded models need a colour per face, not a painting. Giving every model
the same small grid of swatches means every model can share one material, so
whatever lands on an avatar together merges into one material slot at build
time with nothing to atlas.

Beside `<stem>.toml` this writes:

    <stem>.png           the swatches, one solid cell each
    <stem>-emission.png  white where a swatch glows, black elsewhere
    <stem>.json          swatch name -> UV of the cell centre, for the model scripts

Cells are solid and a model samples the centre of its cell, so mip-mapping,
bilinear filtering and atlas padding can never bleed a neighbour in.
"""

import json
import pathlib
import sys
import tomllib

from PIL import Image


def bake(source: pathlib.Path) -> None:
    spec = tomllib.loads(source.read_text(encoding="utf-8"))
    columns, cell = spec["columns"], spec["cell"]
    rows = spec["row"]
    # Power-of-two sides, so the importer never rescales the cells; unused
    # rows at the bottom stay black and are free to fill later.
    grid_rows = 1
    while grid_rows < len(rows):
        grid_rows *= 2
    if columns & (columns - 1):
        raise SystemExit(f"{source}: columns must be a power of two, got {columns}")
    size = (columns * cell, grid_rows * cell)

    colour = Image.new("RGB", size, "#000000")
    emission = Image.new("L", size, 0)
    swatches = {}

    for row_index, row in enumerate(rows):
        names = row["swatches"]
        if len(names) > columns:
            raise SystemExit(f"{source}: row {row_index} has {len(names)} swatches; the grid is {columns} wide")
        for column_index, entry in enumerate(names):
            name, hex_colour = entry
            if name in swatches:
                raise SystemExit(f"{source}: swatch {name!r} is defined twice")
            box = (column_index * cell, row_index * cell, (column_index + 1) * cell, (row_index + 1) * cell)
            colour.paste(hex_colour, box)
            if row.get("emissive", False):
                emission.paste(255, box)
            swatches[name] = {
                "color": hex_colour,
                # Unity's V axis runs bottom-up; the image is laid out top-down.
                "uv": [
                    round((column_index + 0.5) / columns, 6),
                    round(1.0 - (row_index + 0.5) / grid_rows, 6),
                ],
                "emissive": bool(row.get("emissive", False)),
            }

    stem = source.with_suffix("")
    colour.save(stem.with_suffix(".png"), optimize=True)
    emission.save(stem.parent / f"{stem.name}-emission.png", optimize=True)
    stem.with_suffix(".json").write_text(
        json.dumps({"size": list(size), "cell": cell, "swatches": swatches}, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"palette: {source.name}: {len(swatches)} swatches, {size[0]}x{size[1]}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    for argument in sys.argv[1:]:
        bake(pathlib.Path(argument))
