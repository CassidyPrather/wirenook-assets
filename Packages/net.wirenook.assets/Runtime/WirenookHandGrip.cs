using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Marks the transform that positions a hand-held prop inside a hand.
    /// Humanoid rigs agree on which bone is the hand but not on how its axes
    /// point, so no single offset suits every avatar; this marker lets the
    /// editor fit the prop to the avatar it is placed on.
    ///
    /// It also says when the prop is in the hand and how the hand closes on
    /// it. The build reads that from every grip on the avatar and gives each
    /// hand a pair of layers in the Gesture playable layer that curl its
    /// fingers round whatever it is holding, in place of the gesture pose
    /// and of finger tracking, for exactly as long as it holds it. A grip
    /// with nothing in <see cref="held"/> is fitted and never posed.
    ///
    /// Stripped from the upload.
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

        [Tooltip("The prop is in the hand while every one of these holds. Leave empty for a grip that needs no finger pose.")]
        public List<WirenookHeldWhen> held = new List<WirenookHeldWhen>();

        [Tooltip("How far the middle, ring and little fingers close: 0 is an open hand, 1 a fist. " +
                 "About 0.6 wraps a handle three centimetres thick.")]
        [Range(0f, 1f)]
        public float curl = 0.6f;

        [Tooltip("How far the index finger closes. Less than the others for a finger that lies along a barrel or rests on a trigger.")]
        [Range(0f, 1f)]
        public float indexCurl = 0.6f;

        [Tooltip("How far the thumb closes over the fingers.")]
        [Range(0f, 1f)]
        public float thumbCurl = 0.8f;
    }

    /// <summary>One animator parameter having one value.</summary>
    [Serializable]
    public sealed class WirenookHeldWhen
    {
        [Tooltip("An animator parameter that is the same for everyone who sees the avatar: synced, or worked out on every client.")]
        public string parameter;

        [Tooltip("An integer parameter that has to equal Value, rather than a bool that has to be on (or, with Not, off).")]
        public bool isInt;

        public int value;

        [Tooltip("The opposite: a bool that is off, or an integer that is anything but Value.")]
        public bool not;

        public static WirenookHeldWhen On(string parameter) => new WirenookHeldWhen { parameter = parameter };
        public static WirenookHeldWhen Off(string parameter) => new WirenookHeldWhen { parameter = parameter, not = true };
        public static WirenookHeldWhen Is(string parameter, int value) => new WirenookHeldWhen { parameter = parameter, isInt = true, value = value };
    }
}
