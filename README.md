# Wirenook Assets

The shared base for wirenook.net VRChat avatar packages: one palette, two
materials, a set of menu icons, a few sound effects, and the editor helpers
the other packages build their prefabs with. It follows the style guide at
<https://wirenook.net/style/>.

Package: `net.wirenook.assets`. Needs the VRChat Avatars SDK 3.10.6 or
newer, [Modular Avatar](https://modular-avatar.nadena.dev/) and
[lilToon](https://lilxyzw.github.io/lilToon/).

## Why one shared set

VRChat ranks avatars on material slots, meshes and texture memory. Props
that each bring their own materials add up quickly. Every wirenook prop
instead maps its faces onto the same small swatch texture and uses the same
material assets, so however many of them land on an avatar they collapse
into one or two material slots when the avatar is optimised at build time,
with nothing to atlas.

## What is in it

| Folder | Contents |
| --- | --- |
| `Textures/` | `wirenook-palette.png`: the site's colours as a grid of solid swatches, with `wirenook-palette-emission.png` marking the row that glows and `wirenook-palette.json` mapping swatch names to UVs. Source: `wirenook-palette.toml`. |
| `Materials/` | `wirenook` (opaque lilToon, palette-coloured, glow row emissive) and `wirenook-glow` (additive and unlit, for beams and halos). |
| `Icons/` | 32 menu icons, 256 px, each an SVG with its PNG export. See `Icons/README.md`. |
| `Sounds/` | Six short effects (`pop-in`, `pop-out`, `click`, `chime`, `portal`, `megaphone`). |
| `Runtime/` | `WirenookFeature`, which marks and describes an optional avatar feature for upload tooling, and `WirenookHandGrip`, which marks where a hand-held prop sits in a hand. Both are editor-only and stripped from uploads. |
| `Editor/` | The helpers listed below. |

### Editor helpers

- **`ControllerBuilder`** builds an animator controller from code and
  keeps its GUID across rebuilds, with the clips stored inside it.
- **`FeaturePrefab`** edits a drop-in prefab in place and adds the Modular
  Avatar wiring (merge animator, parameters, menu items, bone proxy).
- **`PopToggle`** is the house toggle: a prop pops in with overshoot and
  shrinks away by scaling a bone. Renderers stay on, so an optimiser can
  merge every prop into one mesh. `FeaturePrefab.Merge` leaves each scaled
  bone resting at the scale its layer starts it at, so props are hidden
  for viewers who block the avatar's animations too.
- **`WirenookPlatform`** says which platform a build is for (the editor's
  build target, or one a build is told to pretend), and the build plugin
  turns the same prefab into an Android or iOS avatar: objects marked
  `WirenookPcOnly` are left out with their parameters kept, hidden UV
  tiles are cut from the mesh, materials become VRChat's mobile shaders,
  and audio sources go. `FeaturePrefab.Describe(..., pcOnly: true)` marks
  a feature that cannot work there.
- **`ToonMaterial`** is the single lilToon configuration all materials
  start from: no minimum brightness, UV tile discard on the second UV set,
  and lilToon's Clipping Canceller switched on for the project.
- **`HandGrip`** fits hand-held props to an avatar from its finger bones.
- **`WirenookSounds`** gives a feature one audio source and plays clips on
  it from animator states.
- **`FeatureTags`** (in `Runtime/Core`, engine-free and unit-tested) turns
  the features on an avatar into VRChat's tag list: required tags first,
  then features by priority, within the ten-tag limit.
- **`WirenookImportRules`** applies import settings by file name to
  palettes, menu icons and prop models in any `net.wirenook.*` package.

### Conventions for props

A prop model is a skinned mesh with a small armature, exported to a
package's `Models/` folder with its materials named `wirenook` and
`wirenook-glow`. Its root bone sits at the hold point. `tools/blender/`
has the Blender library the props are built and validated with.

### Project settings this package changes

Rebuilding the materials turns on lilToon's **Clipping Canceller** in the
project's shader settings (`ProjectSettings/lilToonSetting.json`). It
pushes geometry that would be cut by the camera's near plane back onto it,
so a face seen from very close does not show its inside. It is a
per-project lilToon setting, not a material property, so a project that
uses these materials needs it enabled once; `ToonMaterial.EnableClippingCanceller()`
does that.

## Regenerating

```bash
python tools/palette.py Packages/net.wirenook.assets/Textures/wirenook-palette.toml
python tools/icons.py          # SVG -> PNG through Inkscape
python tools/sfx.py            # synthesises Sounds/*.wav
```

then `Tools > Wirenook > Assets > Rebuild Materials` in Unity. The tools
need Python 3.11+, Pillow and numpy; `icons.py` needs Inkscape on the PATH.

The sounds are synthesised from sine partials and filtered noise by
`tools/sfx.py`; nothing is sampled.

## Development

Requires Unity 2022.3.22f1 (see `ProjectSettings/ProjectVersion.txt`) via a VPM
client (prefer [ALCOM](https://vrc-get.anatawa12.com/en/alcom/)), and the
[.NET SDK](https://dotnet.microsoft.com/download) 11 for the headless checks.

The Unity project at the repository root is the development harness. The
packages built on this one expect it checked out beside them, which is the
layout [cass-avatar](https://github.com/CassidyPrather/cass-avatar) provides
by mounting them all as submodules under `modules/`.

```bash
scripts/check-meta.sh          # every visible file has a committed .meta
scripts/check-manifests.py     # package.json sanity across Packages/
scripts/format.sh --check      # dotnet-format over the CI-visible C#
scripts/test.sh                # headless NUnit run of Runtime/Core + Tests
scripts/build-packages.sh      # VPM .zip + .unitypackage per package -> dist/
```

Pre-commit hook: `git config core.hooksPath .githooks`

Release: bump `version` in the package's `package.json`, commit and push,
then create a GitHub release tagged with that version. CI attaches the `.zip`
and `.unitypackage`; the listing at
[CassidyPrather/vpm](https://github.com/CassidyPrather/vpm) picks them up
once this repository is in its `githubRepos`.

## Legal

Copyright 2026 Cassidy Prather <cassidy@wirenook.net>

Everything in this repository henceforth is licensed under
[AGPLv3](https://www.gnu.org/licenses/agpl-3.0.html) (See `LICENSE`) unless
otherwise specified.

As a special exception, the copyright holders give permission to link this
Program with the VRChat SDK and with Unity Technologies' proprietary engine
components, and to convey the resulting work under terms of your choice,
provided that you also convey the Corresponding Source of this Program under
the terms of the GNU Affero General Public License version 3 in all other
respects. (This exception also in `LICENSE_ADDENDUM`.)

## Credits

Descended from https://github.com/vrchat-community/template-package
([VRCHAT DISTRO LICENSE FILE](https://github.com/vrchat-community/template-package/blob/d9cf13fe9f56867cbf7315a4dbbf1901bc1537ec/Packages/com.vrchat.core.bootstrap/License.md))

Third-party material is inventoried in [CREDITS.md](CREDITS.md).
