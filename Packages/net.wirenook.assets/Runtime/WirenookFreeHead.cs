using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// On an avatar's root: this rig's humanoid head bone carries nothing.
    /// No vertex is weighted to it and nothing hangs under it; the head
    /// people see is carried by another bone, <see cref="visual"/>, which
    /// follows the humanoid one's turning by a constraint.
    ///
    /// VRChat puts a player's voice and nameplate at the humanoid head
    /// bone. On a rig built this way the bone can be moved without the
    /// avatar looking any different, which is how a feature makes its
    /// wearer silent for some of the people in a room
    /// (<see cref="WirenookBoneTarget"/>). A feature that wants to do that
    /// looks for this mark, and leaves itself out of an avatar without it.
    ///
    /// The build also moves everything that features attach to the head
    /// (Modular Avatar bone proxies) to <see cref="visual"/>, so that a hat,
    /// a sign or the origin of a pointer stays where the head is seen to
    /// be.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Free Head")]
    public sealed class WirenookFreeHead : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("The bone that carries the head people see.")]
        public Transform visual;
    }
}
