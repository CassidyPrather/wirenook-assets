using NUnit.Framework;
using Wirenook.Assets.Core;

namespace Wirenook.Assets.Tests
{
    public class SignatureColourTests
    {
        private const float Close = 0.0005f;

        private static float[] Turned(float red, float green, float blue, float signature)
        {
            SignatureColour.Turn(ref red, ref green, ref blue, signature);
            return new[] { red, green, blue };
        }

        [Test]
        public void NoTurnAndAFullTurnLeaveAColourAsItIs()
        {
            foreach (var signature in new[] { 0f, 1f })
            {
                var colour = Turned(0.2f, 0.9f, 1f, signature);
                Assert.AreEqual(0.2f, colour[0], Close);
                Assert.AreEqual(0.9f, colour[1], Close);
                Assert.AreEqual(1f, colour[2], Close);
            }
        }

        [Test]
        public void AThirdOfATurnHandsEachChannelOn()
        {
            var colour = Turned(0.2f, 0.9f, 1f, 1f / 3f);
            Assert.AreEqual(1f, colour[0], Close);
            Assert.AreEqual(0.2f, colour[1], Close);
            Assert.AreEqual(0.9f, colour[2], Close);
        }

        [Test]
        public void GreyStaysGrey()
        {
            var colour = Turned(0.4f, 0.4f, 0.4f, 0.37f);
            Assert.AreEqual(0.4f, colour[0], Close);
            Assert.AreEqual(0.4f, colour[1], Close);
            Assert.AreEqual(0.4f, colour[2], Close);
        }

        [Test]
        public void ATurnKeepsTheSumOfTheChannels()
        {
            for (var step = 0; step <= 16; step++)
            {
                var colour = Turned(0.17f, 0.9f, 1f, step / 16f);
                Assert.AreEqual(2.07f, colour[0] + colour[1] + colour[2], 0.002f);
            }
        }

        [Test]
        public void TheChosenColourAtRestIsThePaintedOne()
        {
            SignatureColour.Chosen(0f, out var red, out var green, out var blue);
            Assert.AreEqual(SignatureColour.PaintedRed, red, 0.002f);
            Assert.AreEqual(SignatureColour.PaintedGreen, green, 0.002f);
            Assert.AreEqual(SignatureColour.PaintedBlue, blue, 0.002f);
        }

        [Test]
        public void TheChosenColourIsAlwaysOneThatCanBeShown()
        {
            for (var step = 0; step <= 32; step++)
            {
                SignatureColour.Chosen(step / 32f, out var red, out var green, out var blue);
                foreach (var channel in new[] { red, green, blue })
                {
                    Assert.GreaterOrEqual(channel, 0f);
                    Assert.LessOrEqual(channel, 1f);
                }
            }
        }

        [Test]
        public void SrgbAndLinearUndoEachOther()
        {
            foreach (var value in new[] { 0f, 0.002f, 0.2f, 0.5f, 0.95f, 1f })
                Assert.AreEqual(value, SignatureColour.ToSrgb(SignatureColour.ToLinear(value)), Close);
        }
    }
}
