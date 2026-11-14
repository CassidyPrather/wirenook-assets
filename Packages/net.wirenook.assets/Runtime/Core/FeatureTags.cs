using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Wirenook.Assets.Core
{
    /// <summary>
    /// Turns the features found on an avatar into the tag list VRChat stores
    /// with the upload. VRChat only accepts author tags from ordinary users,
    /// caps them at <see cref="Limit"/>, and lower-cases them, so the result is
    /// deterministic: required tags first, then features by priority.
    /// </summary>
    public static class FeatureTags
    {
        /// <summary>The prefix VRChat requires on every tag a creator sets.</summary>
        public const string Prefix = "author_tag_";

        /// <summary>How many author tags VRChat accepts on one avatar.</summary>
        public const int Limit = 10;

        public readonly struct Candidate
        {
            public readonly string Tag;
            public readonly int Priority;

            public Candidate(string tag, int priority)
            {
                Tag = tag;
                Priority = priority;
            }
        }

        /// <summary>
        /// Reduces free text to the form the SDK itself would send: lower-case,
        /// spaces as underscores, nothing but letters, digits and underscores.
        /// Returns the bare tag, without <see cref="Prefix"/>.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (raw == null) return "";
            var text = raw.Trim();
            if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) text = text.Substring(Prefix.Length);

            var builder = new StringBuilder(text.Length);
            foreach (var character in text.ToLowerInvariant())
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                    builder.Append(character);
                else if (character == ' ' || character == '_' || character == '-')
                    builder.Append('_');
            }

            return builder.ToString().Trim('_');
        }

        /// <summary>The tag as VRChat stores it.</summary>
        public static string ToApi(string raw)
        {
            var bare = Normalize(raw);
            return bare.Length == 0 ? "" : Prefix + bare;
        }

        /// <summary>
        /// Builds the final API tag list. Required tags always survive; optional
        /// ones fill what is left of the limit, highest priority first, ties
        /// broken alphabetically so the same avatar always yields the same list.
        /// </summary>
        /// <param name="required">Tags every upload must carry.</param>
        /// <param name="optional">Feature tags, each with a priority.</param>
        /// <param name="dropped">Optional tags that did not fit.</param>
        public static List<string> Compose(
            IEnumerable<string> required,
            IEnumerable<Candidate> optional,
            out List<string> dropped)
        {
            var result = new List<string>();
            foreach (var tag in required.Select(ToApi))
            {
                if (tag.Length > 0 && !result.Contains(tag)) result.Add(tag);
            }

            if (result.Count > Limit)
            {
                throw new ArgumentException(
                    "There are " + result.Count + " required tags, but VRChat accepts only " + Limit + ".");
            }

            dropped = new List<string>();
            var ranked = optional
                .Select(candidate => new Candidate(ToApi(candidate.Tag), candidate.Priority))
                .Where(candidate => candidate.Tag.Length > 0)
                .GroupBy(candidate => candidate.Tag)
                .Select(group => new Candidate(group.Key, group.Max(candidate => candidate.Priority)))
                .OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Tag, StringComparer.Ordinal);

            foreach (var candidate in ranked)
            {
                if (result.Contains(candidate.Tag)) continue;
                if (result.Count < Limit) result.Add(candidate.Tag);
                else dropped.Add(candidate.Tag);
            }

            return result;
        }
    }
}
