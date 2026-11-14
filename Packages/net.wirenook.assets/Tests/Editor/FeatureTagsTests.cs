using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Wirenook.Assets.Core;

namespace Wirenook.Assets.Tests
{
    public class FeatureTagsTests
    {
        private static FeatureTags.Candidate Tag(string tag, int priority = 0)
        {
            return new FeatureTags.Candidate(tag, priority);
        }

        [Test]
        public void NormalizesToTheFormVRChatStores()
        {
            Assert.AreEqual("agplv3", FeatureTags.Normalize("AGPLv3"));
            Assert.AreEqual("laser_pointer", FeatureTags.Normalize(" Laser Pointer "));
            Assert.AreEqual("catch_up", FeatureTags.Normalize("catch-up!"));
            Assert.AreEqual("ai", FeatureTags.Normalize("author_tag_ai"));
            Assert.AreEqual("", FeatureTags.Normalize(null));
        }

        [Test]
        public void PrefixesApiTags()
        {
            Assert.AreEqual("author_tag_agplv3", FeatureTags.ToApi("AGPLv3"));
            Assert.AreEqual("", FeatureTags.ToApi("???"));
        }

        [Test]
        public void RequiredTagsComeFirstAndSurvive()
        {
            var optional = Enumerable.Range(0, 20).Select(i => Tag("feature " + i, i));
            var tags = FeatureTags.Compose(new[] { "ai", "AGPLv3" }, optional, out var dropped);

            Assert.AreEqual(FeatureTags.Limit, tags.Count);
            Assert.AreEqual("author_tag_ai", tags[0]);
            Assert.AreEqual("author_tag_agplv3", tags[1]);
            Assert.AreEqual(12, dropped.Count);
        }

        [Test]
        public void HigherPriorityFeaturesWinTheRemainingSlots()
        {
            var optional = Enumerable.Range(0, 20).Select(i => Tag("feature " + i, i));
            var tags = FeatureTags.Compose(new[] { "ai", "AGPLv3" }, optional, out var dropped);

            CollectionAssert.Contains(tags, "author_tag_feature_19");
            CollectionAssert.Contains(tags, "author_tag_feature_12");
            CollectionAssert.Contains(dropped, "author_tag_feature_11");
        }

        [Test]
        public void OrderIsDeterministic()
        {
            var first = FeatureTags.Compose(new[] { "ai" }, new[] { Tag("b"), Tag("a"), Tag("c", 5) }, out _);
            var second = FeatureTags.Compose(new[] { "ai" }, new[] { Tag("c", 5), Tag("a"), Tag("b") }, out _);

            CollectionAssert.AreEqual(first, second);
            CollectionAssert.AreEqual(new[] { "author_tag_ai", "author_tag_c", "author_tag_a", "author_tag_b" }, first);
        }

        [Test]
        public void DuplicatesAndEmptyTagsAreIgnored()
        {
            var tags = FeatureTags.Compose(
                new[] { "ai", "AI" },
                new[] { Tag("ai", 3), Tag("  "), Tag("Flight"), Tag("flight", 9) },
                out var dropped);

            CollectionAssert.AreEqual(new[] { "author_tag_ai", "author_tag_flight" }, tags);
            Assert.AreEqual(0, dropped.Count);
        }

        [Test]
        public void TooManyRequiredTagsIsAnError()
        {
            var required = Enumerable.Range(0, FeatureTags.Limit + 1).Select(i => "required " + i);
            Assert.Throws<ArgumentException>(() => FeatureTags.Compose(required, new List<FeatureTags.Candidate>(), out _));
        }
    }
}
