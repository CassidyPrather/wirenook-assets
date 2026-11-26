using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Marks a feature as one way of showing that the wearer is away: a nap,
    /// a crash, a vanishing. An avatar is meant to do one of them at a time,
    /// and says which with <see cref="WirenookAfkChoice"/>; the build then
    /// leaves the others out. Where an avatar makes no choice, every marked
    /// feature on it is built, as a feature dropped onto an avatar by hand
    /// always was.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook AFK")]
    public sealed class WirenookAfk : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("Which way of being away this is: \"nap\", \"crash\", \"vanish\".")]
        public string flavour;
    }
}
