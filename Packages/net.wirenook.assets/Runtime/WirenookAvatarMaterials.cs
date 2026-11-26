using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Stands in for every renderer on the avatar. A feature that changes how
    /// the whole avatar is drawn (a glitch, a sign that is part of the
    /// avatar's own mesh) cannot know the avatar's renderers by path: it is a
    /// drop-in prefab. It animates material properties on this object
    /// instead, as if it were a renderer, and the build gives the same curves
    /// to each renderer on the avatar that has a material with that property.
    ///
    /// The object needs no renderer of its own; the curves are bound to
    /// <c>SkinnedMeshRenderer</c> by convention and are moved, never played
    /// here. On an avatar none of whose materials has the property, they
    /// animate nothing.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Avatar Materials")]
    public sealed class WirenookAvatarMaterials : MonoBehaviour, IWirenookEditorOnly
    {
    }
}
