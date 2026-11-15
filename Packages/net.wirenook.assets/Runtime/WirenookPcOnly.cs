using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Marks an object that Android and iOS builds leave out: something that
    /// cannot work there (a custom shader, a light, a toggle that relies on a
    /// PC shader feature) or that the avatar cannot afford under the mobile
    /// performance limits.
    ///
    /// A mobile build removes the object and everything under it, except the
    /// expression parameters it declares. Those stay, in the same order,
    /// because VRChat syncs parameters by position: an avatar whose platforms
    /// disagree on the list shows other people the wrong toggles.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook PC Only")]
    public sealed class WirenookPcOnly : MonoBehaviour, IWirenookEditorOnly
    {
    }
}
