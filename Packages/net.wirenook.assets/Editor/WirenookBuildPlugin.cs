using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using Wirenook.Assets.Editor;

[assembly: ExportsPlugin(typeof(WirenookBuildPlugin))]

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// This package's steps in the non-destructive avatar build. They run on
    /// the throwaway copy the build works on, never on the prefab.
    ///
    /// The point of both steps is to let an optimiser merge meshes. It will
    /// only merge renderers that are alike in every way it can see: the same
    /// root bone, bounds and light-probe anchor, the same animated material
    /// properties, and no component nearby that it does not recognise.
    /// </summary>
    public sealed class WirenookBuildPlugin : Plugin<WirenookBuildPlugin>
    {
        public override string QualifiedName => "net.wirenook.assets";
        public override string DisplayName => "Wirenook Assets";

        private const string ModularAvatar = "nadena.dev.modular-avatar";

        /// <summary>What was found on the avatar before anything moved it around.</summary>
        private sealed class Joining
        {
            public SkinnedMeshRenderer Body;
            public readonly List<SkinnedMeshRenderer> Props = new List<SkinnedMeshRenderer>();
        }

        protected override void Configure()
        {
            // Features are found by walking down from their marker, so this
            // has to happen first: Modular Avatar later moves hand-held props
            // out from under their feature and onto the hand bones.
            InPhase(BuildPhase.Resolving).Run("Find props that join the avatar mesh", context =>
            {
                var root = context.AvatarRootObject;
                var features = root.GetComponentsInChildren<WirenookFeature>(true);
                var owned = new HashSet<SkinnedMeshRenderer>(
                    features.SelectMany(feature => feature.GetComponentsInChildren<SkinnedMeshRenderer>(true)));
                var joining = context.GetState<Joining>();
                joining.Props.AddRange(features
                    .Where(feature => feature.joinAvatarMesh)
                    .SelectMany(feature => feature.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    .Distinct());

                // The avatar's own meshes are whatever no feature brought.
                // Props join the part without blendshapes: optimisers keep
                // meshes with blendshapes apart, since every extra vertex in
                // such a mesh makes each expression cost more.
                joining.Body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer => !owned.Contains(renderer) && renderer.sharedMesh != null)
                    .OrderBy(renderer => renderer.sharedMesh.blendShapeCount > 0)
                    .ThenByDescending(renderer => renderer.sharedMesh.vertexCount)
                    .FirstOrDefault();
            });

            var afterModularAvatar = InPhase(BuildPhase.Transforming).AfterPlugin(ModularAvatar);
            afterModularAvatar.WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
            {
                sequence.Run("Join props to the avatar mesh", context =>
                {
                    var joining = context.GetState<Joining>();
                    if (joining.Body == null || joining.Props.Count == 0) return;
                    Join(context.Extension<AnimatorServicesContext>(), joining);
                });
            });

            afterModularAvatar.Run("Remove editor-only markers", context =>
            {
                foreach (var marker in context.AvatarRootObject.GetComponentsInChildren<IWirenookEditorOnly>(true))
                    Object.DestroyImmediate((Component)marker);
            });
        }

        private static void Join(AnimatorServicesContext animators, Joining joining)
        {
            var body = joining.Body;
            var bodyPath = animators.ObjectPathRemapper.GetVirtualPathForObject(body.gameObject);

            // Whatever animates the body's materials (outfit tile switches,
            // for instance) will animate the merged mesh, props included. Say
            // so up front by giving the props the same curves; a prop has no
            // faces in any switchable tile, so they change nothing it shows.
            var shared = new List<(VirtualClip clip, EditorCurveBinding binding, AnimationCurve curve)>();
            foreach (var clip in animators.AnimationIndex.GetClipsForObjectPath(bodyPath))
            {
                foreach (var binding in clip.GetFloatCurveBindings())
                {
                    if (binding.path == bodyPath && binding.type == typeof(SkinnedMeshRenderer) &&
                        binding.propertyName.StartsWith("material.", System.StringComparison.Ordinal))
                        shared.Add((clip, binding, clip.GetFloatCurve(binding)));
                }
            }

            // The optimiser also compares what each animated property rests
            // at. The body's material may rest with some tiles hidden (skin
            // under clothing); a prop's shared material rests with all shown.
            // Give the props copies that rest the same way. The copies exist
            // only in this build; the shared material assets are untouched.
            var reference = body.sharedMaterials.FirstOrDefault(material => material != null);
            var properties = shared.Select(entry => entry.binding.propertyName.Substring("material.".Length)).Distinct().ToList();
            var matched = new Dictionary<Material, Material>();

            foreach (var prop in joining.Props.Where(prop => prop != null))
            {
                var materials = prop.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || reference == null) continue;
                    if (!matched.TryGetValue(materials[i], out var copy))
                    {
                        var source = materials[i];
                        var differing = properties
                            .Where(property => reference.HasProperty(property) && source.HasProperty(property) &&
                                               !Mathf.Approximately(source.GetFloat(property), reference.GetFloat(property)))
                            .ToList();
                        copy = source;
                        if (differing.Count > 0)
                        {
                            copy = new Material(source) { name = source.name };
                            foreach (var property in differing) copy.SetFloat(property, reference.GetFloat(property));
                        }

                        matched[source] = copy;
                    }

                    materials[i] = copy;
                }

                prop.sharedMaterials = materials;

                prop.rootBone = body.rootBone;
                prop.localBounds = body.localBounds;
                prop.probeAnchor = body.probeAnchor;
                prop.updateWhenOffscreen = body.updateWhenOffscreen;
                prop.lightProbeUsage = body.lightProbeUsage;
                prop.reflectionProbeUsage = body.reflectionProbeUsage;
                prop.shadowCastingMode = body.shadowCastingMode;
                prop.receiveShadows = body.receiveShadows;
                prop.allowOcclusionWhenDynamic = body.allowOcclusionWhenDynamic;
                prop.quality = body.quality;
                prop.skinnedMotionVectors = body.skinnedMotionVectors;

                var path = animators.ObjectPathRemapper.GetVirtualPathForObject(prop.gameObject);
                foreach (var (clip, binding, curve) in shared)
                    clip.SetFloatCurve(path, binding.type, binding.propertyName, curve);
            }
        }
    }
}
