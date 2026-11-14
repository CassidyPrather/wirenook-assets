#!/usr/bin/env python3
"""Exports the radial-menu icons and draws their contact sheet.

    tools/icons.py                 # every Icons/*.svg -> Icons/<stem>.png
    tools/icons.py --check         # write nothing; exit 1 if an export is missing or stale
    tools/icons.py --sheet out.png # contact sheet somewhere else

Each `<name>.svg` under Packages/net.wirenook.assets/Icons/ is exported with
Inkscape to `<name>.png` beside it: 256x256, RGBA, transparent. VRChat's
expression menu wants exactly that, and Unity imports the PNG, not the SVG.

The contact sheet shows every icon at 64 px (about the size the menu draws
it) on the darkest and lightest surface it has to survive. It is written to
`Icons/.contact-sheet.png`: the leading dot hides it from Unity, so it needs
no .meta and never ships as a texture.

Needs Inkscape 1.x on PATH (or named by $INKSCAPE) and Pillow 10.1 or later.
"""

import argparse
import io
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree

from PIL import Image, ImageDraw, ImageFont

ICONS = pathlib.Path(__file__).resolve().parent.parent / "Packages" / "net.wirenook.assets" / "Icons"
SIZE = 256
# Clear space the finished drawing keeps from every canvas edge: art that
# runs to the edge looks cut off in the menu, and usually means the violet
# extrusion has been clipped.
MARGIN = 16
NAME = re.compile(r"[a-z0-9]+(-[a-z0-9]+)*")

# Windows caps a command line near 32k characters; file names are passed
# relative to a scratch directory, so a batch this size stays far below it.
BATCH = 100

# The two surfaces an icon has to read on: VRChat's wheel at its darkest, and
# the site's panel colour, which is also the icons' own fill.
SHEET_DARK = "#1a2026"
SHEET_LIGHT = "#d8f7ff"
SHEET_ICON = 64
SHEET_COLUMNS = 8
SHEET_CELL = (116, 104)
SHEET_MARGIN = 16


class IconError(Exception):
    """A problem with one icon, already phrased for the person fixing it."""


def find_inkscape() -> str:
    command = os.environ.get("INKSCAPE") or shutil.which("inkscape")
    if not command:
        raise IconError("Inkscape not found: put it on PATH or set INKSCAPE to the executable")
    return command


def check_source(svg: pathlib.Path) -> None:
    """Rejects sources that would export to the wrong shape or name."""
    if not NAME.fullmatch(svg.stem):
        raise IconError(f"{svg.name}: icon names are lowercase kebab-case")
    try:
        root = ElementTree.parse(svg).getroot()
    except ElementTree.ParseError as error:
        raise IconError(f"{svg.name}: not well-formed XML ({error})") from error
    view_box = (root.get("viewBox") or "").replace(",", " ").split()
    try:
        numbers = [float(value) for value in view_box]
    except ValueError:
        numbers = []
    if numbers != [0, 0, SIZE, SIZE]:
        raise IconError(f'{svg.name}: viewBox must be "0 0 {SIZE} {SIZE}", got {root.get("viewBox")!r}')


def canonical_png(svg: pathlib.Path, raw: pathlib.Path) -> bytes:
    """Validates one Inkscape export and re-encodes it.

    Inkscape's own file carries resolution metadata that differs between
    versions; re-encoding through Pillow drops it, so an unchanged SVG always
    yields the same bytes and a second run rewrites nothing.
    """
    if not raw.exists():
        raise IconError(f"{svg.name}: Inkscape produced no image")
    image = Image.open(raw).convert("RGBA")
    if image.size != (SIZE, SIZE):
        raise IconError(f"{svg.name}: exported {image.size[0]}x{image.size[1]}, expected {SIZE}x{SIZE}")
    drawn = image.getchannel("A").getbbox()
    if drawn is None:
        raise IconError(f"{svg.name}: export is fully transparent")
    clear = min(drawn[0], drawn[1], SIZE - drawn[2], SIZE - drawn[3])
    if clear < MARGIN:
        raise IconError(f"{svg.name}: drawing comes within {clear} px of the canvas edge; keep {MARGIN} px clear")
    encoded = io.BytesIO()
    image.save(encoded, format="PNG", optimize=True)
    return encoded.getvalue()


def rasterise(inkscape: str, sources: list[pathlib.Path]) -> dict[pathlib.Path, bytes | IconError]:
    """Renders the sources and returns PNG bytes, or the reason, for each.

    The sources are copied to a scratch directory and exported there, so
    nothing in Icons/ changes until the result is known to be good (and
    --check can compare without writing). One Inkscape process takes the whole
    batch: start-up is most of the cost, and parallel instances race each
    other on the session bus and crash at random.
    """
    results: dict[pathlib.Path, bytes | IconError] = {}
    with tempfile.TemporaryDirectory() as scratch:
        scratch_dir = pathlib.Path(scratch)
        for start in range(0, len(sources), BATCH):
            batch = sources[start : start + BATCH]
            for svg in batch:
                shutil.copyfile(svg, scratch_dir / svg.name)
            process = subprocess.run(
                [
                    inkscape,
                    "--export-type=png",
                    "--export-area-page",
                    f"--export-width={SIZE}",
                    f"--export-height={SIZE}",
                    "--export-background-opacity=0",
                    *(svg.name for svg in batch),
                ],
                cwd=scratch_dir,
                capture_output=True,
                text=True,
            )
            detail = (process.stderr or process.stdout).strip()
            for svg in batch:
                try:
                    results[svg] = canonical_png(svg, scratch_dir / f"{svg.stem}.png")
                except IconError as error:
                    results[svg] = IconError(f"{error}: {detail}") if detail else error
    return results


def write_if_changed(path: pathlib.Path, data: bytes) -> bool:
    if path.exists() and path.read_bytes() == data:
        return False
    path.write_bytes(data)
    return True


def contact_sheet(icons: list[tuple[str, bytes]]) -> bytes:
    """Every icon at menu size, once on the dark surface and once on the light."""
    font = ImageFont.load_default(size=11)
    cell_width, cell_height = SHEET_CELL
    rows = -(-len(icons) // SHEET_COLUMNS)
    panel = (SHEET_COLUMNS * cell_width + 2 * SHEET_MARGIN, rows * cell_height + 2 * SHEET_MARGIN)
    sheet = Image.new("RGB", (panel[0], panel[1] * 2))
    draw = ImageDraw.Draw(sheet)

    for panel_index, (surface, label_colour) in enumerate(((SHEET_DARK, "#d8f7ff"), (SHEET_LIGHT, "#271452"))):
        top = panel_index * panel[1]
        sheet.paste(surface, (0, top, panel[0], top + panel[1]))
        for index, (name, data) in enumerate(icons):
            icon = Image.open(io.BytesIO(data)).resize((SHEET_ICON, SHEET_ICON), Image.LANCZOS)
            left = SHEET_MARGIN + (index % SHEET_COLUMNS) * cell_width
            cell_top = top + SHEET_MARGIN + (index // SHEET_COLUMNS) * cell_height
            sheet.paste(icon, (left + (cell_width - SHEET_ICON) // 2, cell_top + 6), icon)
            draw.text(
                (left + cell_width // 2, cell_top + SHEET_ICON + 14),
                name,
                font=font,
                fill=label_colour,
                anchor="ma",
            )

    encoded = io.BytesIO()
    sheet.save(encoded, format="PNG", optimize=True)
    return encoded.getvalue()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--icons", type=pathlib.Path, default=ICONS, help="directory of <name>.svg sources")
    parser.add_argument("--sheet", type=pathlib.Path, help="contact sheet path (default: <icons>/.contact-sheet.png)")
    parser.add_argument("--check", action="store_true", help="write nothing; fail if any export is missing or stale")
    arguments = parser.parse_args()

    icons = arguments.icons
    sheet_path = arguments.sheet or icons / ".contact-sheet.png"
    sources = sorted(icons.glob("*.svg"))
    if not sources:
        print(f"icons: no .svg sources in {icons}", file=sys.stderr)
        return 1

    problems: list[str] = []
    valid = []
    for svg in sources:
        try:
            check_source(svg)
            valid.append(svg)
        except IconError as error:
            problems.append(str(error))

    try:
        rendered = rasterise(find_inkscape(), valid)
    except IconError as error:
        print(f"icons: {error}", file=sys.stderr)
        return 1

    good: list[tuple[str, bytes]] = []
    stale = 0
    for svg in valid:
        data = rendered[svg]
        if isinstance(data, IconError):
            problems.append(str(data))
            continue
        png = svg.with_suffix(".png")
        if arguments.check:
            current = png.exists() and png.read_bytes() == data
            stale += not current
            print(f"icons: {png.name}: {'up to date' if current else 'STALE'}")
        else:
            print(f"icons: {png.name}: {'exported' if write_if_changed(png, data) else 'unchanged'}")
        good.append((svg.stem, data))

    # An export whose source is gone would keep shipping in the package.
    for png in sorted(icons.glob("*.png")):
        if not png.name.startswith(".") and not png.with_suffix(".svg").exists():
            problems.append(f"{png.name}: no {png.stem}.svg source beside it")

    if problems:
        for problem in problems:
            print(f"icons: {problem}", file=sys.stderr)
        print(f"icons: {len(problems)} problem(s); contact sheet left alone", file=sys.stderr)
        return 1

    sheet = contact_sheet(good)
    if arguments.check:
        sheet_current = sheet_path.exists() and sheet_path.read_bytes() == sheet
        stale += not sheet_current
        print(f"icons: {sheet_path.name}: {'up to date' if sheet_current else 'STALE'}")
        print(f"icons: {len(good)} icons checked, {stale} file(s) stale")
        return 1 if stale else 0

    print(f"icons: {sheet_path.name}: {'written' if write_if_changed(sheet_path, sheet) else 'unchanged'} ({len(good)} icons)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
