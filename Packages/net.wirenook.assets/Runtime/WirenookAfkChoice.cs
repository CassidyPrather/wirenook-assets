using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// On an avatar's root: which of the away features on it
    /// (<see cref="WirenookAfk"/>) a build keeps. The others are left out,
    /// their expression parameters excepted, which stay where they were so
    /// that the list comes out the same on every platform.
    ///
    /// Android and iOS have a choice of their own, for an avatar whose way
    /// of being away cannot be drawn there.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook AFK Choice")]
    public sealed class WirenookAfkChoice : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("The flavour PC builds keep.")]
        public string flavour;

        [Tooltip("The flavour Android and iOS builds keep; the same as on PC when left empty.")]
        public string mobileFlavour;

        /// <summary>The flavour a build for mobile, or not, keeps.</summary>
        public string For(bool mobile) => mobile && !string.IsNullOrEmpty(mobileFlavour) ? mobileFlavour : flavour;
    }
}
