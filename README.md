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
| `Icons/` | 50 menu icons, 256 px, each an SVG with its PNG export. See `Icons/README.md`. |
| `Sounds/` | Ten short effects (`pop-in`, `pop-out`, `click`, `chime`, `portal`, `megaphone`, `ping`, `bonk`, `poof`, `collect`), all at one loudness. |
| `Runtime/` | `WirenookFeature`, which marks and describes an optional avatar feature for upload tooling; `WirenookHandGrip`, which marks where a hand-held prop sits in a hand; and `WirenookHandClaim`, by which a feature says it uses a hand's gestures as buttons while one of its parameters is on, so that an avatar can keep its face still for that hand. All are editor-only and stripped from uploads. |
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
  and audio sources, lights and colliders go. `FeaturePrefab.Describe(..., pcOnly: true)` marks
  a feature that cannot work there.
- **`ToonMaterial`** is the single lilToon configuration all materials
  start from: no minimum brightness, UV tile discard on the second UV set,
  and lilToon's Clipping Canceller switched on for the project. It also
  makes a surface mark the stencil where it is drawn (see below).
- **`HandGrip`** fits hand-held props to an avatar from its finger bones.
- **`WirenookSounds`** gives a feature one audio source and plays clips on
  it from animator states, all at one volume. Its `Gain` is the one number
  that makes every sound louder or quieter.
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

### The stencil bit

Every opaque surface `ToonMaterial.Configure` sets up, which is the
`wirenook` material and any avatar built on it, sets stencil bit 128
(`ToonMaterial.AvatarStencil`) on the pixels it draws, outline included,
with a write mask of 128 so the other seven bits are left as they were.
`wirenook-glow` does not: light is not part of the figure. The bit is how a
later effect tells this avatar from the room behind it; the mosaic censor in
[party-tricks](https://github.com/CassidyPrather/party-tricks) draws only
where it is set, and its README lists what follows from that. Nothing else
reads it. A stencil effect of someone else's that compares all eight bits
will see its value changed by 128 where one of these surfaces is drawn.

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

### How loud the sounds are

`tools/sfx.py` writes every sound at the same loudness, so that features
can play them all at one volume and none drowns another. The measure is the
level of a sound's loudest 200 ms after K-weighting (the filter of ITU-R
BS.1770, which follows the ear better than plain RMS), and each sound is
brought to -17 dB relative to full scale on it. The megaphone is 2 dB over
the rest, because being heard is its job. No sample goes above -6 dB:
VRChat runs avatar audio through a compressor and
[advises](https://creators.vrchat.com/worlds/components/vrc_spatialaudiosource/)
files normalised to between -6 and -12 dB. The click is too short to reach
the shared loudness under that ceiling and is about 3 dB quieter. The tool
prints the loudness and peak of each file it writes.

`WirenookSounds` then plays them at full volume through a VRC Spatial Audio
Source with 6 dB of gain, heard out to 12 m. VRChat allows an avatar 10 dB
and 40 m. To change how loud everything is, change `WirenookSounds.Gain`
and rebuild the prefabs of each package that plays a sound; to change one
sound against the rest, give it an entry in `TRIM` in `tools/sfx.py`.

The levels are measured, not listened to in a busy instance. Two things
the measure does not cover: the laser pointer's ping is heard out to 30 m
instead of 12, so it falls off more slowly with distance than the others,
and how VRChat's compressor treats these is not known.

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
