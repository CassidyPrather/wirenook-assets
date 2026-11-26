using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Marks a feature's audio source as one that could as well be another
    /// feature's: a cue heard from the avatar itself, short, and not
    /// expected to play over anything. The build keeps one such source for
    /// the whole avatar and points every feature's sounds at it, because
    /// VRChat ranks an avatar on how many sources it carries and a set of
    /// features that each bring one adds up quickly.
    ///
    /// Two cues that land together cut one another off. A source that has
    /// to be somewhere in particular (on a thing left standing in the
    /// world), or that plays something long (a loop), is not marked and is
    /// kept as it is.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Shared Sound")]
    public sealed class WirenookSharedSound : MonoBehaviour, IWirenookEditorOnly
    {
    }
}
