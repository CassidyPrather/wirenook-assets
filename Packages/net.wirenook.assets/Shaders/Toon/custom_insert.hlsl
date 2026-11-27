// The work behind the hooks declared in custom.hlsl. Included in every pass
// after lilToon's common code and before the pass itself.
#ifndef WIRENOOK_TOON_INCLUDED
#define WIRENOOK_TOON_INCLUDED

#include "Packages/net.wirenook.assets/Shaders/WirenookSignature.hlsl"

// lilToon leaves the vertex id out of the vertex data where a platform
// cannot supply it (it says so before this file is read). The glitch then
// tells vertices apart by where they are.
#if defined(LIL_NOT_SUPPORT_VERTEXID)
    #define WIRENOOK_VERTEX_ID(input) 0u
#else
    #define WIRENOOK_VERTEX_ID(input) input.vertexID
#endif

float wirenookHash(float2 seed)
{
    return frac(sin(dot(seed, float2(127.1, 311.7))) * 43758.5453);
}

float3 wirenookHash3(float2 seed)
{
    return frac(sin(float3(dot(seed, float2(127.1, 311.7)), dot(seed, float2(269.5, 183.3)), dot(seed, float2(419.2, 371.9)))) * 43758.5453);
}

// The avatar's size against the size it was modelled at, and where the
// renderer stands: the effects are measured in the avatar's own metres, so
// a scaled avatar glitches and signs in proportion.
float wirenookScale()
{
    return max(length(float3(LIL_MATRIX_M._m00, LIL_MATRIX_M._m10, LIL_MATRIX_M._m20)), 1e-5);
}

// A sign's vertex leaves the pivot it is stored at for its place on a board
// that faces the viewer. "The viewer" is the point between the eyes in a
// headset, so both eyes see one board, not two.
void wirenookSign(inout float4 positionOS, float2 place, float2 which)
{
    float scale = wirenookScale();
    float3 pivot = mul(LIL_MATRIX_M, float4(positionOS.xyz, 1.0)).xyz;
    float3 toViewer = lilHeadDirection(pivot);
    float3 level = float3(toViewer.x, 0.0, toViewer.z);
    float away = length(level);
    float3 toward = level / max(away, 1e-4);
    // Unity is left-handed: with the board's face towards the viewer, this is the viewer's right.
    float3 right = cross(toward, float3(0.0, 1.0, 0.0));

    float number = -which.x;
    float show = max(abs(number - _WirenookSign) < 0.5 ? _WirenookSignShow : 0.0,
        max(abs(number - _WirenookSignB) < 0.5 ? _WirenookSignBShow : 0.0,
            abs(number - _WirenookSignC) < 0.5 ? _WirenookSignCShow : 0.0));
    // The layer, and how far out the sign stands, in one number.
    float reach = floor(which.y / 16.0) * 0.01;
    float layer = which.y - floor(which.y / 16.0) * 16.0;
    // Someone standing in the avatar, its wearer above all, would have the
    // board in their face or behind their head. It folds away for them.
    show *= smoothstep(0.45, 0.7, away / scale);

    float3 position = pivot
        + toward * (reach + layer * 0.004) * show * scale
        + (right * place.x + float3(0.0, 1.0, 0.0) * place.y) * show * scale;
    positionOS.xyz = mul(LIL_MATRIX_I_M, float4(position, 1.0)).xyz;
}

// A part's vertex leaves the point it is stored at for its own place, as far
// as its part's amount says.
void wirenookPart(inout float4 positionOS, float2 place, float2 which)
{
    float number = -which.x;
    float4 mine = float4(abs(number - 1.0) < 0.5, abs(number - 2.0) < 0.5, abs(number - 3.0) < 0.5, abs(number - 4.0) < 0.5);
    float3 offset = float3(place.x, place.y, -which.y - 16.0);
    // Two speeds that never line up, and each part out of step with the others.
    float2 lean = float2(sin(_Time.y * 6.3 + number * 1.7), cos(_Time.y * 4.9 + number * 2.3));
    offset.xz += lean * _WirenookPartsSway * max(offset.y, 0.0);
    positionOS.xyz += offset * dot(mine, _WirenookParts);
}

void wirenookGlitch(inout float4 positionOS, uint vertexID)
{
    float amount = saturate(_WirenookGlitch);
    float scale = wirenookScale();
    float3 origin = float3(LIL_MATRIX_M._m03, LIL_MATRIX_M._m13, LIL_MATRIX_M._m23);
    float3 position = (mul(LIL_MATRIX_M, float4(positionOS.xyz, 1.0)).xyz - origin) / scale;

    // Everything is held between ticks and jumps on them. Eased, it would
    // read as wobble; held, it reads as a machine failing.
    float tick = floor(_Time.y * 11.0);

    // Crunch: now and then the whole surface snaps to a coarse grid.
    float crunch = step(1.0 - 0.4 * amount, wirenookHash(float2(floor(_Time.y * 5.0), 17.0)));
    float grid = lerp(0.012, 0.055, wirenookHash(float2(tick, 3.0)));
    position = lerp(position, round(position / grid) * grid, crunch);

    // Tear: level slices of the figure slide sideways, a different set each tick.
    float slice = floor(position.y * 8.0 + wirenookHash(float2(tick, 5.0)) * 8.0);
    float3 roll = wirenookHash3(float2(slice, tick));
    float torn = step(1.0 - 0.25 * amount, roll.x);
    float turn = roll.z * 6.2831853;
    position += float3(cos(turn), 0.0, sin(turn)) * torn * (0.04 + 0.2 * roll.y) * amount;

    // Spikes: a vertex in a few hundred is thrown clear, dragging its triangles with it.
    #if defined(LIL_NOT_SUPPORT_VERTEXID)
        float2 id = floor(positionOS.xy * 613.0) + floor(positionOS.z * 211.0);
    #else
        float2 id = float2(float(vertexID % 4096u), float(vertexID / 4096u));
    #endif
    float3 thrown = wirenookHash3(id + floor(_Time.y * 6.0) * float2(7.31, 3.17));
    float spike = step(1.0 - 0.006 * amount, thrown.x);
    position += normalize(wirenookHash3(id * 1.7 + 0.5) - 0.5) * spike * (0.1 + 0.3 * thrown.y) * amount;

    positionOS.xyz = mul(LIL_MATRIX_I_M, float4(origin + position * scale, 1.0)).xyz;
}

void wirenookVertex(inout float4 positionOS, float2 place, float2 which, uint vertexID)
{
    // A sign's number is stored negative. A mesh without a fourth UV set is
    // handed one of its lower sets in its place, by the graphics API and by
    // optimisers that merge meshes alike, and none of those is ever negative.
    if (which.x < -0.5)
    {
        if (which.y < -0.5)
        {
            wirenookPart(positionOS, place, which);
        }
        else
        {
            // A sign stays whole while the figure behind it falls apart: it is what says why.
            wirenookSign(positionOS, place, which);
        }
    }
    else if (_WirenookGlitch > 0.0)
    {
        wirenookGlitch(positionOS, vertexID);
    }
}

void wirenookSignature(inout float3 colour, float2 uv)
{
    if (_WirenookSignature <= 0.0) return;
    float painted = LIL_SAMPLE_2D(_WirenookSignatureMask, sampler_MainTex, uv).r;
    if (painted <= 0.0) return;
    colour = lerp(colour, wirenookSignatureTurn(colour, _WirenookSignature), painted);
}

// An eight by eight sad face, a row per entry from the top, a bit per pixel from the left.
static const float wirenookSadFace[8] = { 0.0, 36.0, 36.0, 0.0, 0.0, 60.0, 66.0, 0.0 };

void wirenookScreen(inout float3 colour, float2 place, float2 uv1)
{
    if (_WirenookScreen <= 0.0 || _WirenookScreenTile < 0.0) return;
    float2 tile = floor(uv1);
    if (abs(tile.x + tile.y * 4.0 - _WirenookScreenTile) > 0.5) return;
    // Stored one to the right and up, so that the set is never negative (see wirenookVertex).
    float2 at = place - 1.0;

    float2 pixel = floor(saturate(at) * 7.999);
    float row = wirenookSadFace[(int)(7.0 - pixel.y)];
    float lit = fmod(floor(row / exp2(7.0 - pixel.x)), 2.0);

    // Not shaded: a screen is its own light, and has to read in a dark room.
    float3 screen = lerp(float3(0.024, 0.34, 0.96), float3(1.0, 1.0, 1.0), lit);
    // A slow roll of brightness down the screen, as on a tube that has lost its signal.
    screen *= 0.88 + 0.12 * step(0.5, frac(at.y * 3.0 - _Time.y * 0.7));
    colour = lerp(colour, screen, saturate(_WirenookScreen));
}

#endif
