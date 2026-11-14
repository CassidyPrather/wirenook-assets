using VRC.SDKBase;

namespace Wirenook.Assets
{
    /// <summary>
    /// A component that only carries information for the editor. VRChat drops
    /// these from the upload; this package also removes them at the very
    /// start of an avatar build, before optimisers run, because an optimiser
    /// that meets a component it does not recognise has to assume the worst
    /// and stops merging meshes around it.
    /// </summary>
    public interface IWirenookEditorOnly : IEditorOnly
    {
    }
}
