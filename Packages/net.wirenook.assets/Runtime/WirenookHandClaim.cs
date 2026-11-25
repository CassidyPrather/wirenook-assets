using UnityEngine;

namespace Wirenook.Assets
{
    public enum WirenookHand
    {
        Left,
        Right,
    }

    /// <summary>
    /// Says that a feature uses one hand's gestures as its buttons while an
    /// animator parameter is on: a trigger pull that pings, or confirms where
    /// something is put down.
    ///
    /// A hand gesture is also how most avatars choose a facial expression, so
    /// without this the click that places a portal is a grin as well. An
    /// avatar that reads these markers treats the hand as resting for as
    /// long as the parameter is on and until the hand relaxes afterwards, and
    /// shows no expression for it. The feature itself goes on reading
    /// VRChat's own gesture parameters.
    ///
    /// The parameter has to be a bool with the same value for everyone who
    /// sees the avatar: a synced one, or one every client works out for
    /// itself. Expressions are played on every viewer's client, and a
    /// parameter only the wearer has would lock them only there.
    ///
    /// Stripped from the upload: it carries no runtime behaviour.
    /// </summary>
    [AddComponentMenu("Wirenook/Wirenook Hand Claim")]
    public sealed class WirenookHandClaim : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("The hand whose gestures the feature uses as buttons.")]
        public WirenookHand hand = WirenookHand.Right;

        [Tooltip("A bool animator parameter that is on, on every viewer's client, while the feature is using the hand.")]
        public string parameter;
    }
}
