using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Marks the root of an optional avatar feature (a gadget, a locomotion
    /// set, an outfit) and says how to present it. Upload tooling collects
    /// these from whatever a variant actually contains, so tags, thumbnails
    /// and build reports describe the avatar as built rather than as planned.
    /// Stripped from the upload: it carries no runtime behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Feature")]
    public sealed class WirenookFeature : MonoBehaviour, IWirenookEditorOnly
    {
        [Tooltip("Stable identifier, lower-case kebab-case, e.g. \"flashlight\".")]
        public string id;

        [Tooltip("Name shown in reports.")]
        public string label;

        [Tooltip("The feature's own icon; usually its menu icon.")]
        public Texture2D icon;

        [Tooltip("Icon that stands for this feature where an avatar's features are shown together and small, " +
                 "such as its thumbnail. Features that share one are shown once, so like features can share.")]
        public Texture2D summaryIcon;

        [Tooltip("Avatar search tag this feature contributes. Leave empty when VRChat already lets " +
                 "people filter for it (performance rank, platform, face tracking).")]
        public string tag;

        [Tooltip("Which tags survive when an avatar has more features than VRChat's ten-tag limit. Higher wins.")]
        public int tagPriority;

        [Tooltip("The feature's skinned meshes stay within reach of the avatar's body (hand tools, things worn), " +
                 "so the build may merge them into the avatar's own mesh. Leave off for anything that can be " +
                 "left behind in the world: merged, it would vanish whenever the avatar itself is out of view.")]
        public bool joinAvatarMesh;
    }
}
