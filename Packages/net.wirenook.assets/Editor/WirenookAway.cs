using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// What the features that show a wearer is away have in common, whatever
    /// they show: they start on VRChat's own <c>AFK</c> or on the menu's
    /// switch (<see cref="WirenookSignals"/>), they put that switch in the
    /// menu under the same name and icon, and each says which flavour it is
    /// so that an avatar carrying several builds one
    /// (<see cref="WirenookAfkChoice"/>).
    /// </summary>
    public static class WirenookAway
    {
        /// <summary>Stands asleep, nodding off (net.wirenook.locomotion).</summary>
        public const string Nap = "nap";

        /// <summary>Crashes: locked, glitching, an error window beside it (net.wirenook.party-tricks).</summary>
        public const string Crash = "crash";

        /// <summary>Is gone in a puff of smoke until the wearer is back (net.wirenook.party-tricks).</summary>
        public const string Vanish = "vanish";

        public static readonly string[] Flavours = { Nap, Crash, Vanish };

        /// <summary>The menu entry's name and icon, the same on every avatar.</summary>
        public const string MenuLabel = "AFK";
        public const string MenuIcon = "away";

        /// <summary>Says which flavour a feature is. On the feature's root.</summary>
        public static void Mark(GameObject root, string flavour)
        {
            FeaturePrefab.Ensure<WirenookAfk>(root).flavour = flavour;
        }

        /// <summary>
        /// Puts the "AFK" switch in the avatar's menu from this object. The
        /// feature declares the parameter itself, with the rest of its own
        /// (<see cref="FeaturePrefab.SyncedBool"/> of <see cref="WirenookSignals.Away"/>).
        /// </summary>
        public static void Menu(GameObject holder)
        {
            FeaturePrefab.InstallMenu(holder);
            FeaturePrefab.MenuToggle(holder, MenuLabel, MenuIcon, WirenookSignals.Away);
        }

        /// <summary>
        /// Hands the whole body from tracking to animation, stops locomotion
        /// and raises the Action playable layer, or hands it all back. For a
        /// state of a controller in the Action layer.
        ///
        /// VRChat's own Action controller does the same for its own
        /// <c>AFK</c>, and hands back when that ends whatever a menu switch
        /// says. A feature that holds the body for the switch as well has to
        /// keep entering states that set this, not set it once.
        /// </summary>
        public static void HandOver(StateBuilder state, bool held, float seconds = 0.2f)
        {
            var tracking = held ? VRC_AnimatorTrackingControl.TrackingType.Animation : VRC_AnimatorTrackingControl.TrackingType.Tracking;
            state.Behaviour<VRCAnimatorTrackingControl>(control =>
            {
                control.trackingHead = tracking;
                control.trackingLeftHand = tracking;
                control.trackingRightHand = tracking;
                control.trackingHip = tracking;
                control.trackingLeftFoot = tracking;
                control.trackingRightFoot = tracking;
                control.trackingLeftFingers = tracking;
                control.trackingRightFingers = tracking;
                control.trackingEyes = tracking;
                control.trackingMouth = tracking;
            });
            state.Behaviour<VRCAnimatorLocomotionControl>(control => control.disableLocomotion = held);
            state.Behaviour<VRCPlayableLayerControl>(control =>
            {
                control.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
                control.goalWeight = held ? 1f : 0f;
                control.blendDuration = seconds;
            });
        }
    }
}
