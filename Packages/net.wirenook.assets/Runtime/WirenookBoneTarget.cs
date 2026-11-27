using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Beside a VRChat constraint on a feature's own object: the constraint
    /// is to move one of the avatar's humanoid bones, not the object it is
    /// on. A drop-in prefab cannot refer to the avatar's bones, so the
    /// build points the constraint's target at the bone. The component
    /// stays where it is, and so do the paths that animate it.
    ///
    /// Moving the head is only safe on a rig whose head bone carries
    /// nothing (<see cref="WirenookFreeHead"/>). On any other avatar the
    /// build removes the object instead, and says so.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Bone Target")]
    public sealed class WirenookBoneTarget : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("The humanoid bone the constraint on this object moves.")]
        public HumanBodyBones bone = HumanBodyBones.Head;
    }
}
