using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Marks the transform that positions a hand-held prop inside a hand.
    /// Humanoid rigs agree on which bone is the hand but not on how its axes
    /// point, so no single offset suits every avatar; this marker lets the
    /// editor fit the prop to the avatar it is placed on. Stripped from the
    /// upload.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Hand Grip")]
    public sealed class WirenookHandGrip : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("The hand this prop is held in.")]
        public HumanBodyBones hand = HumanBodyBones.RightHand;

        [Tooltip("Metres from the wrist toward the fingertips.")]
        public float alongFingers = 0.07f;

        [Tooltip("Metres from the hand's surface into the palm.")]
        public float intoPalm = 0.03f;
    }
}
