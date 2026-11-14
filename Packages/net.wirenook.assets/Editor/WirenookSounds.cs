using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// The shared sound effects (synthesised by tools/sfx.py) and the one way
    /// features play them. A feature gets a single audio source and plays
    /// clips on it from animator states, so adding sounds never adds sources:
    /// VRChat ranks avatars on how many sources they carry.
    /// </summary>
    public static class WirenookSounds
    {
        public const string Folder = "Packages/net.wirenook.assets/Sounds";
        public const string SourceName = "SFX";

        public static AudioClip Get(string name)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/" + name + ".wav");
        }

        /// <summary>
        /// The feature's audio source, created under <paramref name="parent"/>
        /// if missing. Quiet and short-ranged: these are cues for the people
        /// standing nearby, not announcements.
        /// </summary>
        public static AudioSource Source(GameObject parent)
        {
            var holder = FeaturePrefab.Child(parent, SourceName);
            var source = FeaturePrefab.Ensure<AudioSource>(holder);
            source.playOnAwake = false;
            source.loop = false;
            source.clip = null;
            source.volume = 0.5f;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;

            var spatial = FeaturePrefab.Ensure<VRCSpatialAudioSource>(holder);
            spatial.Gain = 0f;
            spatial.Near = 0f;
            spatial.Far = 12f;
            spatial.EnableSpatialization = true;
            return source;
        }

        /// <summary>Plays a clip on the feature's source each time <paramref name="state"/> is entered.</summary>
        /// <param name="sourcePath">Animation path of the source object, relative to the merged controller's root.</param>
        public static void PlayOnEnter(StateBuilder state, string sourcePath, string clipName, float volume = 0.5f)
        {
            var clip = Get(clipName);
            if (clip == null)
                throw new System.IO.FileNotFoundException("No shared sound named \"" + clipName + "\"; run tools/sfx.py.", Folder);

            state.Behaviour<VRCAnimatorPlayAudio>(play =>
            {
                play.SourcePath = sourcePath;
                play.Clips = new[] { clip };
                play.ClipsApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
                play.Volume = new Vector2(volume, volume);
                play.VolumeApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
                // A few cents of drift keeps repeated cues from sounding mechanical.
                play.Pitch = new Vector2(0.97f, 1.03f);
                play.PitchApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
                play.Loop = false;
                play.LoopApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
                play.PlayOnEnter = true;
                play.StopOnEnter = true;
            });
        }
    }
}
