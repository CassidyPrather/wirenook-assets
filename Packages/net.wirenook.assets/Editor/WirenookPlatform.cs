using System;
using UnityEditor;

namespace Wirenook.Assets.Editor
{
    /// <summary>The platforms VRChat takes separate avatar builds for.</summary>
    public enum Platform
    {
        Pc,
        Android,
        Ios,
    }

    /// <summary>
    /// Which platform a build is for. Normally that is the editor's active
    /// build target, which is also what the VRChat SDK builds and uploads
    /// for. A build can be told to pretend otherwise, so the mobile result
    /// can be measured and looked at without waiting for the editor to
    /// reimport the whole project for another target.
    /// </summary>
    public static class WirenookPlatform
    {
        private static Platform? _pretended;

        /// <summary>The editor's active build target.</summary>
        public static Platform Active
        {
            get
            {
                switch (EditorUserBuildSettings.activeBuildTarget)
                {
                    case BuildTarget.Android: return Platform.Android;
                    case BuildTarget.iOS: return Platform.Ios;
                    default: return Platform.Pc;
                }
            }
        }

        /// <summary>The platform builds are made for right now.</summary>
        public static Platform Current => _pretended ?? Active;

        /// <summary>Android and iOS share one set of rules: shaders, components and performance limits.</summary>
        public static bool IsMobile => Current != Platform.Pc;

        public static Platform Parse(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "pc":
                case "windows": return Platform.Pc;
                case "android":
                case "quest": return Platform.Android;
                case "ios": return Platform.Ios;
                default: throw new ArgumentException("Unknown platform \"" + name + "\"; use pc, android or ios.");
            }
        }

        public static string Name(Platform platform) => platform.ToString().ToLowerInvariant();

        /// <summary>What the editor targets, and what builds pretend to be for if that differs.</summary>
        public static string Show()
        {
            return Name(Active) + (_pretended.HasValue && _pretended != Active ? " (builds pretend " + Name(Current) + ")" : "");
        }

        /// <summary>
        /// Makes builds behave as if the editor targeted another platform,
        /// until this is called with an empty name or scripts reload.
        /// </summary>
        /// <returns>The platform builds are now made for.</returns>
        public static string Pretend(string name)
        {
            _pretended = string.IsNullOrWhiteSpace(name) ? (Platform?)null : Parse(name);
            return Name(Current);
        }

        /// <summary>
        /// Switches the editor's build target, which reimports every asset
        /// for it and reloads scripts. Unity keeps a cache per target, so only
        /// the first switch to each is slow.
        /// </summary>
        public static string Switch(string name)
        {
            var platform = Parse(name);
            _pretended = null;
            if (platform == Active) return Name(platform) + " (already active)";

            bool switched;
            switch (platform)
            {
                case Platform.Android:
                    switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                    break;
                case Platform.Ios:
                    switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
                    break;
                default:
                    switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
                    break;
            }

            if (!switched)
                throw new InvalidOperationException("Unity would not switch to " + Name(platform) +
                                                    "; is that platform's build support installed?");
            return Name(platform);
        }
    }
}
