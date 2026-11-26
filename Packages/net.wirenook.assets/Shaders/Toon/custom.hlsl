// Wirenook's additions to lilToon, as a lilToon custom shader. This file is
// included before lilToon's own sources and only declares: the properties,
// the vertex data the additions read, and the hooks lilToon calls. What the
// hooks do is in custom_insert.hlsl, which is included after lilToon's
// common code, where Unity's variables and lilToon's helpers exist.
//
// Three things are added. Each rests at zero and then costs a comparison.
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
    float _WirenookSignCShow;

#define LIL_CUSTOM_VERTEX_OS \
    wirenookVertex(positionOS, input.uv2, input.uv3, WIRENOOK_VERTEX_ID(input));

// lilToon's passes that shade a surface fill `fd`; the others (shadows,
// depth) have no colour to replace.
#define BEFORE_OUTPUT \
    wirenookScreen(fd.col.rgb, fd.uv2, fd.uv1);
