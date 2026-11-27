using System;

namespace Wirenook.Assets.Core
{
    /// <summary>
    /// The arithmetic of the signature colour: the one colour a wearer
    /// chooses in game, which the trim of their avatar and everything it
    /// carries or sets down then takes.
    ///
    /// Everything that follows it is painted in one colour,
    /// <see cref="PaintedRed"/>, <see cref="PaintedGreen"/> and
    /// <see cref="PaintedBlue"/> (a cyan), or in lighter and darker shades of
    /// its hue. The wearer's choice is an amount from 0 to 1, a share of a
    /// full turn of the hue: at 0 (and at 1) the paint is shown as it is.
    ///
    /// The turn is a rotation of the colour about the grey axis, in linear
    /// light. That is what VRChat's mobile Toon Standard shader does for its
    /// hue shift, so a PC shader that does this and a mobile material that
    /// uses that shader's show the same colour for the same amount. It is
    /// not a turn in HSV: a third of a turn swaps the channels round
    /// exactly, but in between a colour may brighten or dim a little.
    /// </summary>
    public static class SignatureColour
    {
        /// <summary>The painted colour, as it is written in a texture (sRGB, 0 to 1): #73f3ff.</summary>
        public const float PaintedRed = 0x73 / 255f;

        public const float PaintedGreen = 0xf3 / 255f;
        public const float PaintedBlue = 0xff / 255f;

        /// <summary>The hue of the painted colour, as a share of a turn (185 degrees).</summary>
        public const float PaintedHue = 185f / 360f;

        /// <summary>Radians in the full turn an amount of 1 stands for.</summary>
        public const float FullTurn = 6.283185307f;

        /// <summary>
        /// Turns a colour in linear light by <paramref name="signature"/> of
        /// a full turn about grey. Nothing is clamped: a channel may come
        /// out a little below 0 or above 1.
        /// </summary>
        public static void Turn(ref float red, ref float green, ref float blue, float signature)
        {
            var angle = signature * FullTurn;
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            const float k = 0.57735027f;

            // Rodrigues' rotation about (k, k, k).
            var dot = k * (red + green + blue);
            var crossRed = k * (blue - green);
            var crossGreen = k * (red - blue);
            var crossBlue = k * (green - red);
            var along = k * dot * (1f - cos);
            var r = red * cos + crossRed * sin + along;
            var g = green * cos + crossGreen * sin + along;
            var b = blue * cos + crossBlue * sin + along;
            red = r;
            green = g;
            blue = b;
        }

        /// <summary>
        /// The colour the wearer has chosen, as it would be written in a
        /// texture (sRGB, each channel clamped to 0 to 1): the painted
        /// colour, turned.
        /// </summary>
        public static void Chosen(float signature, out float red, out float green, out float blue)
        {
            red = ToLinear(PaintedRed);
            green = ToLinear(PaintedGreen);
            blue = ToLinear(PaintedBlue);
            Turn(ref red, ref green, ref blue, signature);
            red = ToSrgb(red);
            green = ToSrgb(green);
            blue = ToSrgb(blue);
        }

        public static float ToLinear(float srgb)
        {
            return srgb <= 0.04045f ? srgb / 12.92f : (float)Math.Pow((srgb + 0.055f) / 1.055f, 2.4);
        }

        public static float ToSrgb(float linear)
        {
            var clamped = Math.Min(1f, Math.Max(0f, linear));
            return clamped <= 0.0031308f ? clamped * 12.92f : 1.055f * (float)Math.Pow(clamped, 1.0 / 2.4) - 0.055f;
        }
    }
}
