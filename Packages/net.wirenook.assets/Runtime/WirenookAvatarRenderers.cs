using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Stands in for every renderer on the avatar, whatever its materials
    /// are. A feature that hides the whole avatar, or redraws all of it,
    /// cannot know the avatar's renderers by path: it is a drop-in prefab.
    /// It animates this object instead, as if it were a skinned mesh, and
    /// the build gives the same curves to each renderer on the avatar:
    ///
    /// <list type="bullet">
    /// <item><c>m_Enabled</c>: whether anything is drawn at all.</item>
    /// <item><c>m_Materials.Array.data[0]</c>, a material: every slot of
    /// every renderer is given it. An optimiser will not merge meshes whose
    /// materials are animated, so this costs an avatar its merged mesh;
    /// see <see cref="swapsAreForMobile"/>.</item>
    /// <item><c>material.*</c>: a material property, on every renderer,
    /// whether or not the material it has at rest declares it (a material
    /// that is swapped in may).</item>
    /// </list>
    ///
    /// <see cref="WirenookAvatarMaterials"/> is the gentler stand-in, for a
    /// property that only the materials declaring it should be given.
    ///
    /// A renderer marked <see cref="WirenookApart"/> is left out: it is not
    /// part of the wearer's figure.
    ///
    /// Nothing else may animate whether a renderer is enabled: a later
    /// layer would undo it for that renderer. Props are shown and hidden by
    /// scale.
    ///
    /// An avatar that most people are not to see can also rest undrawn
    /// (<see cref="restHidden"/>): the build then leaves every renderer
    /// disabled, and only the feature's animation draws anything.
    ///
    /// Android and iOS draw with VRChat's own shaders only, none of which
    /// can be made to look like something else by a property. So a feature
    /// that redraws the avatar usually does it two ways: by a property of
    /// the avatar's own shader on PC, through
    /// <see cref="WirenookAvatarMaterials"/>, and by swapping a material
    /// in on mobile, through this, marked <see cref="swapsAreForMobile"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Avatar Renderers")]
    public sealed class WirenookAvatarRenderers : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("The materials animated here, and the material properties, are for Android and iOS: the materials have " +
                 "VRChat's mobile shaders. A PC build drops those animations, and so keeps the avatar's meshes mergeable.")]
        public bool swapsAreForMobile;

        [Tooltip("Leave every renderer disabled as the avatar rests, so that where its animations do not run " +
                 "(a viewer's safety settings, the first frame) nothing is drawn. For an avatar that is hidden from most people.")]
        public bool restHidden;
    }
}
