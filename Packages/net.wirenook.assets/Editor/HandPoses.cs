using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Closes a hand round what it holds. Run on the copy an avatar build
    /// works on, before Modular Avatar: it reads every
    /// <see cref="WirenookHandGrip"/> that says when its prop is held, and
    /// hands Modular Avatar one small controller per hand to merge into the
    /// Gesture playable layer.
    ///
    /// Each controller is two layers. The second holds the finger poses, one
    /// state per prop, masked to that hand's fingers and resting at no
    /// weight, so that while the hand is empty it changes nothing: the
    /// gesture poses and finger tracking go on as they were. The first
    /// watches the props' parameters. While one is held it raises the pose
    /// layer and hands that hand's fingers from tracking to animation, and
    /// when it is put away it does the reverse. One state machine decides
    /// for the whole hand, so two tools cannot disagree about who has the
    /// fingers.
    ///
    /// A feature only has to mark its grip. New props get their finger pose
    /// by being described, not by being wired.
    /// </summary>
    public static class HandPoses
    {
        /// <summary>Seconds the fingers take to close on a prop, and to let go.</summary>
        private const float Close = 0.12f;

        /// <summary>What every pose clip's name starts with; the hand and the feature's id follow.</summary>
        public const string ClipPrefix = "hand pose ";

        private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        /// <returns>How many grips were given a pose.</returns>
        public static int Build(GameObject avatar)
        {
            var posed = avatar.GetComponentsInChildren<WirenookHandGrip>(true)
                .Where(grip => grip.held != null && grip.held.Count > 0 && grip.held.All(when => !string.IsNullOrWhiteSpace(when.parameter)))
                .ToList();
            foreach (var hand in posed.GroupBy(grip => grip.hand == HumanBodyBones.LeftHand))
            {
                var side = hand.Key ? "Left" : "Right";
                var holder = new GameObject("Hand Poses " + side);
                holder.transform.SetParent(avatar.transform, false);
                var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
                merge.animator = Controller(side, hand.ToList());
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.Gesture;
                // Muscles have no path to make relative.
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.matchAvatarWriteDefaults = true;
                merge.deleteAttachedAnimator = false;
            }

            return posed.Count;
        }

        private static AnimatorController Controller(string side, List<WirenookHandGrip> grips)
        {
            var builder = ControllerBuilder.InMemory("hand poses " + side.ToLowerInvariant());
            foreach (var when in grips.SelectMany(grip => grip.held))
            {
                if (when.isInt) builder.Int(when.parameter);
                else builder.Bool(when.parameter);
            }

            // The control layer is the first and the poses the second: the
            // control's behaviours name the pose layer by its place, and
            // Modular Avatar moves that along when it merges.
            var control = builder.Layer("Hand Poses " + side);
            var poses = builder.Layer("Hand Poses " + side + " Fingers", weight: 0f, mask: FingerMask(side));

            var free = control.State("Free");
            Hand(free, side, held: false);
            // The pose layer starts here, and nothing leads back: by the time a
            // prop is put away the layer has no weight, and a pose left in it
            // is not seen.
            poses.State("Open");

            for (var i = 0; i < grips.Count; i++)
            {
                var grip = grips[i];
                // Named for the feature the prop belongs to, so that a built avatar can be read.
                var feature = grip.GetComponentInParent<WirenookFeature>(true);
                var name = "Held " + (feature != null && !string.IsNullOrEmpty(feature.id) ? feature.id : i.ToString());
                var holding = control.State(name);
                Hand(holding, side, held: true);
                Conditions(free.To(holding), grip.held, false);
                // Let go as soon as any one of them stops holding.
                var release = holding.To(free);
                for (var c = 0; c < grip.held.Count; c++)
                {
                    if (c > 0) release.Or();
                    Condition(release, grip.held[c], true);
                }

                var pose = poses.State(name, builder.Clip(ClipPrefix + side.ToLowerInvariant() + " " + name.Substring("Held ".Length), clip => Pose(clip, side, grip)));
                Conditions(poses.Any(pose).Fade(Close), grip.held, false);
            }

            return builder.Controller;
        }

        private static void Conditions(TransitionBuilder transition, List<WirenookHeldWhen> all, bool negate)
        {
            foreach (var when in all) Condition(transition, when, negate);
        }

        private static void Condition(TransitionBuilder transition, WirenookHeldWhen when, bool negate)
        {
            var wanted = when.not == negate;
            if (when.isInt)
            {
                if (wanted) transition.Equal(when.parameter, when.value);
                else transition.NotEqual(when.parameter, when.value);
            }
            else if (wanted)
            {
                transition.IfTrue(when.parameter);
            }
            else
            {
                transition.IfFalse(when.parameter);
            }
        }

        /// <summary>Raises or drops the pose layer, and says who moves this hand's fingers.</summary>
        private static void Hand(StateBuilder state, string side, bool held)
        {
            state.Behaviour<VRCAnimatorLayerControl>(control =>
            {
                control.playable = VRC_AnimatorLayerControl.BlendableLayer.Gesture;
                control.layer = 1;
                control.goalWeight = held ? 1f : 0f;
                control.blendDuration = Close;
            });
            state.Behaviour<VRCAnimatorTrackingControl>(control =>
            {
                var fingers = held ? VRC_AnimatorTrackingControl.TrackingType.Animation : VRC_AnimatorTrackingControl.TrackingType.Tracking;
                if (side == "Left") control.trackingLeftFingers = fingers;
                else control.trackingRightFingers = fingers;
            });
        }

        /// <summary>
        /// The fingers' muscles for one grip. A muscle runs from -1, fully
        /// closed, to 1, stretched open, with a relaxed finger near 0; the
        /// middle joint does most of the closing round a handle and the tip
        /// the least.
        /// </summary>
        private static void Pose(ClipBuilder clip, string side, WirenookHandGrip grip)
        {
            void Muscle(string finger, string part, float value) =>
                clip.Constant("", typeof(Animator), side + "Hand." + finger + "." + part, value);

            foreach (var finger in Fingers)
            {
                var curl = finger == "Index" ? grip.indexCurl : grip.curl;
                Muscle(finger, "1 Stretched", Mathf.Lerp(0.6f, -0.85f, curl));
                Muscle(finger, "2 Stretched", Mathf.Lerp(0.7f, -1f, curl));
                Muscle(finger, "3 Stretched", Mathf.Lerp(0.7f, -0.8f, curl));
                // Fingers on a handle lie together.
                Muscle(finger, "Spread", Mathf.Lerp(0f, -0.35f, curl));
            }

            Muscle("Thumb", "1 Stretched", Mathf.Lerp(0.5f, -0.9f, grip.thumbCurl));
            Muscle("Thumb", "2 Stretched", Mathf.Lerp(0.6f, -0.8f, grip.thumbCurl));
            Muscle("Thumb", "3 Stretched", Mathf.Lerp(0.6f, -0.8f, grip.thumbCurl));
            // Across the palm, towards the fingers it closes over. For the
            // thumb the spread muscle runs the other way from the fingers':
            // up is in.
            Muscle("Thumb", "Spread", Mathf.Lerp(0f, 0.8f, grip.thumbCurl));
        }

        private static AvatarMask FingerMask(string side)
        {
            var mask = new AvatarMask { name = "hand poses " + side.ToLowerInvariant() };
            for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, false);
            mask.SetHumanoidBodyPartActive(side == "Left" ? AvatarMaskBodyPart.LeftFingers : AvatarMaskBodyPart.RightFingers, true);
            return mask;
        }
    }
}
