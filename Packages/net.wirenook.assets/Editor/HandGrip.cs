using System;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Fits hand-held props to an avatar's hands. The pose is worked out from
    /// where the finger bones are rather than from the hand bone's own axes,
    /// which differ from rig to rig: forward is the way the fingers point, up
    /// is the back of the hand.
    /// </summary>
    public static class HandGrip
    {
        [MenuItem("Tools/Wirenook/Assets/Fit Hand Grips To Selected Avatar")]
        private static void FitSelected()
        {
            var avatar = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<Animator>() : null;
            if (avatar == null || !avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Fit hand grips", "Select a humanoid avatar in the scene first.", "OK");
                return;
            }

            var count = FitAll(avatar.gameObject, avatar.GetBoneTransform);
            Debug.Log("[wirenook] Fitted " + count + " hand grip(s) on " + avatar.name + ".");
        }

        /// <summary>Fits every grip under <paramref name="avatar"/>.</summary>
        /// <param name="bone">Finds a humanoid bone on that avatar; null if the rig lacks it.</param>
        /// <returns>How many grips were fitted.</returns>
        public static int FitAll(GameObject avatar, Func<HumanBodyBones, Transform> bone)
        {
            var grips = avatar.GetComponentsInChildren<WirenookHandGrip>(true);
            foreach (var grip in grips) Fit(grip, bone);
            return grips.Length;
        }

        public static void Fit(WirenookHandGrip grip, Func<HumanBodyBones, Transform> bone)
        {
            var left = grip.hand == HumanBodyBones.LeftHand;
            var hand = Require(bone, grip.hand);
            var index = Require(bone, left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var middle = Require(bone, left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var little = Require(bone, left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);

            var fingers = (middle.position - hand.position).normalized;
            var towardThumb = (index.position - little.position).normalized;
            // The knuckle line and the finger direction span the palm; which
            // way their cross product faces flips between the two hands.
            var palm = Vector3.Cross(fingers, towardThumb).normalized * (left ? -1f : 1f);

            var position = hand.position + fingers * grip.alongFingers + palm * grip.intoPalm;
            var rotation = Quaternion.LookRotation(fingers, -palm);

            // The grip's parent is moved onto the hand bone at build time with
            // no offset of its own, so the pose is stored in the bone's space.
            Undo.RecordObject(grip.transform, "Fit hand grip");
            grip.transform.localPosition = hand.InverseTransformPoint(position);
            grip.transform.localRotation = Quaternion.Inverse(hand.rotation) * rotation;
            EditorUtility.SetDirty(grip.transform);
        }

        private static Transform Require(Func<HumanBodyBones, Transform> bone, HumanBodyBones which)
        {
            var transform = bone(which);
            if (transform == null)
                throw new InvalidOperationException("The avatar has no " + which + " bone; hand grips need finger bones to fit against.");
            return transform;
        }
    }
}
