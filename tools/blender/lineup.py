"""Renders wirenook props side by side at true scale, to judge them as one family.

    blender -b --python tools/blender/lineup.py -- --out DIR [--name NAME] [--unlit] [FILE.blend ...]

With no files it takes every `blender/*.blend` of the repositories checked out
beside this one. Writes `<name>-sky.png` and `<name>-night.png` into DIR.
`--unlit` leaves beams, halos and other light effects out of the picture.
"""

import argparse
import pathlib
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import wirenook_props

SIBLINGS = pathlib.Path(__file__).resolve().parents[3]


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="lineup.py", description=__doc__,
                                     formatter_class=argparse.RawTextHelpFormatter)
    parser.add_argument("--out", required=True, type=pathlib.Path)
    parser.add_argument("--name", default="lineup")
    parser.add_argument("--width", type=int, default=2400)
    parser.add_argument("--height", type=int, default=1100)
    parser.add_argument("--unlit", action="store_true")
    parser.add_argument("blends", nargs="*", type=pathlib.Path)
    args = parser.parse_args(argv)

    blends = [blend.resolve() for blend in args.blends] or sorted(SIBLINGS.glob("*/blender/*.blend"))
    if not blends:
        raise SystemExit(f"no prop sources found under {SIBLINGS}; build them first")
    wirenook_props.render_lineup(blends, args.out, args.name, width=args.width, height=args.height,
                                 lit=not args.unlit)


if __name__ == "__main__":
    main()
