using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// On a feature's root: this feature does the job of others, which a
    /// build that has this one leaves out (their expression parameters
    /// excepted, as with <see cref="WirenookPcOnly"/>). It is for an avatar
    /// made from another avatar, which cannot take off a feature that one
    /// carries: a fuller version of the feature is added, and says what it
    /// stands in for.
    ///
    /// A feature that is itself left out of a build replaces nothing there.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Replaces")]
    public sealed class WirenookReplaces : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("The ids (WirenookFeature.id) of the features this one stands in for.")]
        public string[] features = new string[0];
    }
}
