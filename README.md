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
| `Icons/` | 73 menu icons, 256 px, each an SVG with its PNG export. See `Icons/README.md`. |
| `Sounds/` | Eighteen short effects (`pop-in`, `pop-out`, `click`, `chime`, `portal`, `megaphone`, `ping`, `bonk`, `poof`, `collect`, `unplug`, `plug`, `sizzle`, `frost`, `splash`, `whoosh`, `boing`, `nom`), all at one loudness, and two written to loop: `buzz`, a quiet hum, and `crackle`, a small fire. |
| `Shaders/Toon/` | `Wirenook/lilToon`: lilToon with three additions, as a lilToon custom shader. See [The shader](#the-shader). |
| `Runtime/` | `WirenookFeature`, which marks and describes an optional avatar feature for upload tooling; `WirenookHandGrip`, which marks where a hand-held prop sits in a hand, when it is held and how the fingers close on it; and `WirenookHandClaim`, by which a feature says it uses a hand's gestures as buttons while one of its parameters is on, so that an avatar can keep its face still for that hand. All are editor-only and stripped from uploads. |
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
- **`SignMesh`** builds a sign: flat artwork in palette colours, with
  block capitals from a five by seven pixel face, that the shader turns to
  face each viewer. **`WirenookPalette`** looks swatches up by name.
- **`OnePieceProp`** rebuilds a prop model as one plain mesh, for a prop
  that is left in the world by an avatar with no material slots to spare.
  `RigidProp` gives every bone's faces a mesh of their own, which is a
  slot each; this folds up to four parts into the root's mesh, each
  collapsed to its bone, and the shader unfolds them by the amounts in
  `_WirenookParts`. A part can then only grow from its point and shrink
  back: it cannot move or turn. PC only, since no mobile shader unfolds
  anything.
- **`StateBuilder.SpeedFrom`** and **`TransitionBuilder.StartAt`**: a
  state whose clip runs at a rate held in a parameter, backwards below
  zero, and a transition that enters a state partway through. Together
  they make a bar that fills and drains (the skill checks of net.wirenook.contacts).
- **`FeaturePrefab.AvatarMaterials`** gives a feature a stand-in for every
  renderer on the avatar (`WirenookAvatarMaterials`). Material properties
  animated on it are given, at build time, to each renderer that has a
  material with the property, so a drop-in prefab can change how the whole
  avatar is drawn without knowing its renderers.
- **`WirenookSignals`** names the animator parameters that mean the same
  thing in every package (`WN/Away`).
- **`HandGrip`** fits hand-held props to an avatar from its finger bones.
- **`HandPoses`** closes a hand round what it holds. A feature marks its
  grip (`WirenookHandGrip`) with when the prop is in the hand (`held`: a
  bool that is on, an integer with a value, or several together) and how
  far the fingers, the index finger and the thumb close. The build then
  gives each hand two layers in the Gesture playable layer: one holds a
  pose per prop, masked to that hand's fingers and resting at no weight;
  the other raises it and hands the fingers from tracking to animation
  for as long as something is held. One state machine per hand decides,
  so two tools cannot disagree, and a new prop gets its pose by being
  described.
- **`WirenookSounds`** gives a feature one audio source and plays clips on
  it from animator states, all at one volume. Its `Gain` is the one number
  that makes every sound louder or quieter. A source made with it is
  marked `WirenookSharedSound` unless told otherwise, and an avatar build
  keeps one such source for all its features and points every sound at
  it: VRChat ranks an avatar on how many sources it carries. Two cues
  that land together cut one another off, so a source that stands in the
  world or plays a loop is made with `shared: false` and kept.
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

### The shader

`ToonMaterial.Configure` sets materials up on `Wirenook/lilToon` (and
`Hidden/Wirenook/lilToonOutline` where there is an outline): lilToon's
opaque shader with this package's hooks compiled in, through lilToon's own
custom shader mechanism (`Shaders/Toon/*.lilcontainer`, `custom.hlsl`,
`custom_insert.hlsl`). Everything lilToon does, its inspector included, is
unchanged. Three properties are added, all resting at nothing:

- **`_WirenookGlitch`** (0 to 1) corrupts the mesh in the vertex stage:
  level slices torn sideways, the surface snapped to a coarse grid in
  bursts, a few vertices thrown out as spikes, re-rolled eleven times a
  second and held in between. Measured in the avatar's own metres, so a
  scaled avatar glitches in proportion.
- **`_WirenookScreen`** (0 to 1) draws a blue error screen with a sad face
  on the faces of one UV discard tile (`_WirenookScreenTile`), unlit.
  Where on the screen a point is comes from the third UV set, which the
  avatar's importer writes. cass uses it for its eyes.
- **`_WirenookParts`** holds four amounts, and grows part 1 to 4 of a
  one-piece prop (`OnePieceProp`) out of its point by that much.
  **`_WirenookPartsSway`** leans the parts to and fro, more the higher
  above its point a vertex is, so a flame's tips move and its foot does
  not.
- **`_WirenookSign`** and **`_WirenookSignShow`** unfold a sign
  (`SignMesh`): geometry whose vertices all rest at one pivot and carry
  their place on the sign in the third UV set, and in the fourth the
  sign's number and, packed into one value, its layer and how far out it
  stands. The shader stands the sign that far from the pivot towards the
  viewer and faces it to them (to the point between the eyes in a
  headset), and folds it away for anyone standing inside the avatar.
  There are three such pairs (`_WirenookSign`, `_WirenookSignB`,
  `_WirenookSignC`), each showing one number at a time: the first is for
  what the wearer is doing (away, busy), the second for what is happening
  to the avatar (reactions), the third for what a tool has to say.
  Features that share a pair take turns; the pairs do not wait on each
  other. Numbers in use: 1 the AFK window (party-tricks), 10 to 22 the
  contact reactions (contacts). A sign's number is stored negative,
  because a mesh with no fourth UV set is given one of its lower sets in
  its place when meshes are merged or drawn, and none of those is ever
  negative. A sign is nothing at all wherever the shader is not this one: on
  a client that blocks the avatar's shaders, and on Android and iOS.

Because it is not one of lilToon's own shader names, tools that know
lilToon by name treat these materials as unknown. Avatar Optimizer then
keeps every vertex channel, which is what the signs need.

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
can play them all at one volume and none drowns another. (`buzz` is the
exception: it is a bed to sit under other things, 10 dB down, and is
written without faded ends so that it loops.) The measure is the
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
