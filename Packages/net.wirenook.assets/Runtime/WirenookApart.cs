using UnityEngine;

namespace Wirenook.Assets
{
    /// <summary>
    /// Marks a renderer that is not part of the wearer's figure: something
    /// set down in the world, or shown to an audience of its own. A feature
    /// that hides the whole avatar or redraws all of it
    /// (<see cref="WirenookAvatarRenderers"/>) leaves it alone, so a game
    /// master who has gone unseen still has their zone markers standing.
    ///
    /// What it draws is then the business of whoever marked it: nothing
    /// else on the avatar will hide it for them.
    ///
    /// Avatar-wide material properties (<see cref="WirenookAvatarMaterials"/>:
    /// the signature colour, say) still reach it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Wirenook/Wirenook Apart")]
    public sealed class WirenookApart : MonoBehaviour, IWirenookEditorOnly
    {
    }
}
