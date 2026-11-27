// The signature colour: one colour the wearer chooses in game, which the
// trim of their avatar and everything it carries or sets down then takes.
// Editor/WirenookSignature.cs says what the convention is; this is the
// arithmetic, for any shader that follows it. It needs nothing but HLSL, so
// it can be included from a lilToon custom shader and from a plain one alike.
//
// A shader that follows the colour declares
//
//     _WirenookSignature ("Signature", Range(0, 1)) = 0
//
// and the build drives that on every renderer whose material has it. The
// amount is a share of a full turn of the hue. A surface painted in the
// signature colour turns its paint with wirenookSignatureTurn; something
// made of light alone can ask for the colour itself, wirenookSignatureColour.

#ifndef WIRENOOK_SIGNATURE_INCLUDED
#define WIRENOOK_SIGNATURE_INCLUDED

// The painted colour, #73f3ff, in linear light.
static const float3 wirenookSignaturePainted = float3(0.1714, 0.8963, 1.0);

// Turns a colour (linear light) about the grey axis by `signature` of a full
// turn. The same arithmetic as the hue shift of VRChat's mobile Toon Standard
// shader, which is what stands in for this on Android and iOS. Never below
// zero: a turn can push a channel out of range.
float3 wirenookSignatureTurn(float3 colour, float signature)
{
    float angle = signature * 6.283185307;
    float s = sin(angle);
    float c = cos(angle);
    const float3 k = float3(0.57735027, 0.57735027, 0.57735027);
    return max(colour * c + cross(k, colour) * s + k * dot(k, colour) * (1.0 - c), 0.0);
}

// The colour the wearer has chosen (linear light).
float3 wirenookSignatureColour(float signature)
{
    return wirenookSignatureTurn(wirenookSignaturePainted, signature);
}

#endif
