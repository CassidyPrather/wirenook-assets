# Credits

Contents, unless otherwise specified, AGPLv3: [LICENSE](LICENSE).
VRChat/Unity linking exception: [LICENSE_ADDENDUM](LICENSE_ADDENDUM).

| Asset | Source | Author | License |
| --- | --- | --- | --- |
| `Packages/com.vrchat.core.bootstrap/` | VRChat's package bootstrapper, vendored unmodified from <https://github.com/vrchat-community/template-package> | VRChat | [VRCHAT DISTRO LICENSE FILE](Packages/com.vrchat.core.bootstrap/License.md) |
| `Packages/net.wirenook.assets/Icons/wirenook.svg`, `carry.svg` and their `.png` exports | Rabbit mark, scaled from `design/wirenook/logo/wirenook-tri.svg` in <https://github.com/CassidyPrather/cassidyprather.github.io> | Cassidy Prather; the site credits [Ron Kyle Almira (Pixel20012)](https://pixel20012.github.io/) for commissioned artwork | AGPLv3 |
| `tools/fonts/AtkinsonHyperlegible-Regular.ttf`, `AtkinsonHyperlegible-Bold.ttf` | Atkinson Hyperlegible 1.006, unmodified; upstream <https://www.brailleinstitute.org/freefont/>. Build-time only: used to letter generated textures, not shipped in the package. | Braille Institute of America, Inc. | [SIL OFL 1.1](tools/fonts/OFL.txt) |
| `rabbit_badge` in `tools/blender/wirenook_props.py` | Rabbit mark, redrawn as flat polygons after `static/images/wirenook/link-charm.png` in <https://github.com/CassidyPrather/cassidyprather.github.io> | Cassidy Prather; the site credits [Ron Kyle Almira (Pixel20012)](https://pixel20012.github.io/) for commissioned artwork | AGPLv3 |
| `Packages/net.wirenook.assets/Shaders/Toon/*.lilcontainer`, `lilCustomShader*.lilblock` | Shader containers after the layout of lilToon 2.4.1's own (`BaseShaderResources/lts.lilinternal`, `lts_o.lilinternal`, `ltspass_opaque.lilinternal`), <https://github.com/lilxyzw/lilToon>. The shader they declare is lilToon itself, taken from the installed package at import; `custom.hlsl` and `custom_insert.hlsl` are this repository's. | lilxyzw | MIT |
