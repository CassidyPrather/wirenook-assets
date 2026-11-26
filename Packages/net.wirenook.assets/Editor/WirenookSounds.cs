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

        /// <summary>
        /// The volume every sound plays at unless a feature says otherwise.
        /// The files are all written at one loudness (tools/sfx.py brings
        /// them to it), so one volume for all of them is what keeps them
        /// balanced; a feature should have a reason to pass another.
        /// VRChat sets the source's volume to this each time a sound plays.
        /// </summary>
        public const float Volume = 1f;

        /// <summary>
        /// How loud the sounds are overall: the boost VRChat adds to the
        /// source, in decibels. This is the one number to change to make
        /// every sound louder or quieter. VRChat caps it at 10 on avatars,
        /// which is also the boost it gives a world's sounds by default, so
        /// at 6 these stay a little under the sounds of the room.
        /// </summary>
        public const float Gain = 6f;

        /// <summary>
        /// How far the sounds carry, in metres. Short-ranged: these are cues
        /// for the people standing nearby, not announcements. VRChat caps
        /// this at 40 on avatars.
        /// </summary>
        public const float Far = 12f;

        public static AudioClip Get(string name)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/" + name + ".wav");
        }

        /// <summary>
        /// The feature's audio source, created under <paramref name="parent"/>
        /// if missing.
        /// </summary>
        public static AudioSource Source(GameObject parent)
        {
            var holder = FeaturePrefab.Child(parent, SourceName);
            var source = FeaturePrefab.Ensure<AudioSource>(holder);
            source.playOnAwake = false;
            source.loop = false;
            source.clip = null;
            source.volume = Volume;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;

            var spatial = FeaturePrefab.Ensure<VRCSpatialAudioSource>(holder);
            spatial.Gain = Gain;
            spatial.Near = 0f;
            spatial.Far = Far;
            spatial.EnableSpatialization = true;
            return source;
        }

        /// <summary>Plays a clip on the feature's source each time <paramref name="state"/> is entered.</summary>
        /// <param name="sourcePath">Animation path of the source object, relative to the merged controller's root.</param>
        /// <param name="volume">Leave out to play at the shared level; see <see cref="Volume"/>.</param>
        /// <param name="loop">
        /// Play it round and round until the next sound on this source, or
        /// <see cref="StopOnEnter"/>, replaces it. For the sounds tools/sfx.py
        /// writes as loops.
        /// </param>
        public static void PlayOnEnter(StateBuilder state, string sourcePath, string clipName, float volume = Volume, bool loop = false)
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
                play.Pitch = loop ? Vector2.one : new Vector2(0.97f, 1.03f);
                play.PitchApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
                play.Loop = loop;
                play.LoopApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
                play.PlayOnEnter = true;
                play.StopOnEnter = true;
            });
        }

        /// <summary>Silences the feature's source each time <paramref name="state"/> is entered.</summary>
        public static void StopOnEnter(StateBuilder state, string sourcePath)
        {
            state.Behaviour<VRCAnimatorPlayAudio>(play =>
            {
                play.SourcePath = sourcePath;
                play.PlayOnEnter = false;
                play.StopOnEnter = true;
                play.LoopApplySettings = VRC_AnimatorPlayAudio.ApplySettings.AlwaysApply;
                play.Loop = false;
            });
        }
    }
}
