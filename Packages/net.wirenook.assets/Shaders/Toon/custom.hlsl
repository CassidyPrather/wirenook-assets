// Wirenook's additions to lilToon, as a lilToon custom shader. This file is
// included before lilToon's own sources and only declares: the properties,
// the vertex data the additions read, and the hooks lilToon calls. What the
// hooks do is in custom_insert.hlsl, which is included after lilToon's
// common code, where Unity's variables and lilToon's helpers exist.
//
// Five things are added. Each rests at zero and then costs a comparison.
//
// _WirenookGlitch (0..1). Corrupts the mesh: horizontal slices torn sideways,
//   the whole surface snapped to a coarse grid in bursts, and a few vertices
//   thrown out as spikes, all re-rolled several times a second.
//
// _WirenookScreen (0..1). Draws a blue error screen with a sad face in place
//   of the surface, on the faces of one UV discard tile (_WirenookScreenTile,
//   column + 4 * row on the second UV set; -1 for none). Where on a screen a
//   point is comes from the third UV set, less one each way: (1,1) to (2,2)
//   across the picture, which is clamped beyond that, so a screen larger
//   than its picture is plain blue around it. The model's importer writes
//   those for each eye.
//
// _WirenookSign and _WirenookSignShow. A sign is geometry that turns to face
//   whoever looks at it and stands a little way out from the avatar towards
//   them. Its vertices all sit at one pivot in the mesh and carry their
//   place on the sign in the third UV set (metres right and up of the pivot)
//   and, in the fourth, which sign they belong to, negated (-1 and down;
//   anything else is ordinary geometry) and, packed into one number, the layer they are on and how
//   far out from the pivot the sign stands (layer + 16 * centimetres). A
//   sign is drawn while _WirenookSign is its number, at the size
//   _WirenookSignShow says (0 folds it away to nothing, 1 is full size).
//
//   There are three such pairs: _WirenookSign, _WirenookSignB and
//   _WirenookSignC, each with its Show. Each shows one sign at a time, on
//   purpose: features that share a pair share the avatar's mesh and
//   material and take turns. The pairs are for kinds of thing that should
//   not have to take turns with each other; ToonMaterial says which is
//   which.
//
// _WirenookParts (four amounts, 0..1 and a little over). A part is a piece
//   of a rigid mesh that grows out of a point: a flame out of its logs. A
//   mesh for each such piece is a material slot for each, and slots are what
//   a prop left standing in the world costs an avatar; this way the prop is
//   one mesh. A part's vertices all sit at its point in the mesh and carry
//   where they belong from there: right and up in the third UV set, and in
//   the fourth which part they are, negated as a sign's number is (-1 to
//   -4), and how far forward, as -(metres + 16), which is what tells a part
//   from a sign. Part n is drawn at the size the nth amount says.
//   _WirenookPartsSway leans every part to and fro, more the higher above
//   its point a vertex is (metres of lean for each metre of height): a
//   flame's tips move and its foot stays put. OnePieceProp makes such meshes.
//
// _WirenookSignature (0..1) and _WirenookSignatureMask. The signature colour:
//   the one colour a wearer chooses in game (Editor/WirenookSignature.cs). The
//   amount is a share of a full turn of the hue, and it turns the texels the
//   mask is white on, which are the ones painted in the signature colour.
//   The mask is read with the main texture's coordinates and sampler, so a
//   pixel-art atlas and its mask line up texel for texel. It is done to the
//   surface's own colour before anything is lit, so shading, and a glow that
//   takes the surface's colour, follow. No mask, or no turn, costs nothing.
//   Shaders/WirenookSignature.hlsl has the arithmetic, for other shaders.

#define LIL_REQUIRE_APP_TEXCOORD1
#define LIL_REQUIRE_APP_TEXCOORD2
#define LIL_REQUIRE_APP_TEXCOORD3
#define LIL_REQUIRE_APP_VERTEXID

#define LIL_CUSTOM_PROPERTIES \
    float _WirenookGlitch; \
    float _WirenookScreen; \
    float _WirenookScreenTile; \
    float _WirenookSign; \
    float _WirenookSignShow; \
    float _WirenookSignB; \
    float _WirenookSignBShow; \
    float _WirenookSignC; \
    float _WirenookSignCShow; \
    float4 _WirenookParts; \
    float _WirenookPartsSway; \
    float _WirenookSignature;

#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_WirenookSignatureMask);

#define LIL_CUSTOM_VERTEX_OS \
    wirenookVertex(positionOS, input.uv2, input.uv3, WIRENOOK_VERTEX_ID(input));

// After the surface's own colour is read, and before a second layer, shading or glow.
#define BEFORE_MAIN2ND \
    wirenookSignature(fd.col.rgb, fd.uvMain);

// lilToon's passes that shade a surface fill `fd`; the others (shadows,
// depth) have no colour to replace.
#define BEFORE_OUTPUT \
    wirenookScreen(fd.col.rgb, fd.uv2, fd.uv1);
