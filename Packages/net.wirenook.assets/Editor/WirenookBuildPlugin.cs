using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
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
    /// Joining props to the avatar's mesh is there to let an optimiser merge
    /// meshes. It will only merge renderers that are alike in every way it
    /// can see: the same root bone, bounds and light-probe anchor, the same
    /// animated material properties, and no component nearby that it does
    /// not recognise.
    ///
    /// The mobile steps turn the same prefab into something Android and iOS
    /// accept. They run only when the build is for one of those (see
    /// <see cref="WirenookPlatform"/>): objects marked PC-only are left out,
    /// hidden UV tiles are cut out of the meshes, since no mobile shader can
    /// discard them, every material is replaced by a VRChat mobile one, and
    /// the components VRChat disables there are removed.
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

        private const string MobileToon = "VRChat/Mobile/Toon Lit";
        private const string MobileGlow = "VRChat/Mobile/Particles/Additive";

        protected override void Configure()
        {
            var beforeModularAvatar = InPhase(BuildPhase.Resolving).BeforePlugin(ModularAvatar);

            // Before Modular Avatar reads anything: what is removed here must
            // contribute no menu, animator or object to the build.
            beforeModularAvatar.Run("Leave PC-only objects out of mobile builds", context =>
            {
                if (WirenookPlatform.IsMobile) RemovePcOnly(context.AvatarRootObject);
            });

            // Features are found by walking down from their marker, so this
            // has to happen first: Modular Avatar later moves hand-held props
            // out from under their feature and onto the hand bones.
            beforeModularAvatar.Run("Find props that join the avatar mesh", context =>
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
            afterModularAvatar.Run("Keep to what mobile builds may carry", context =>
            {
                if (!WirenookPlatform.IsMobile) return;
                MobileMaterials(context.AvatarRootObject);
                RemoveDisabledComponents(context.AvatarRootObject);
            });
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
                    UnityEngine.Object.DestroyImmediate((Component)marker);
            });
        }

        /// <summary>
        /// Removes every object marked <see cref="WirenookPcOnly"/>, leaving
        /// in its place an empty object that declares the same expression
        /// parameters. VRChat syncs parameters by their position in the
        /// list, so the list has to come out the same on every platform even
        /// when what the parameters drive does not exist on this one.
        /// </summary>
        private static void RemovePcOnly(GameObject avatar)
        {
            foreach (var marker in avatar.GetComponentsInChildren<WirenookPcOnly>(true))
            {
                // Already gone with a marked ancestor.
                if (marker == null || marker.gameObject == avatar) continue;
                var target = marker.gameObject;
                var declared = target.GetComponentsInChildren<ModularAvatarParameters>(true)
                    .SelectMany(component => component.parameters)
                    .ToList();
                if (declared.Count > 0)
                {
                    var stand = new GameObject(target.name);
                    stand.transform.SetParent(target.transform.parent, false);
                    stand.transform.SetSiblingIndex(target.transform.GetSiblingIndex());
                    stand.AddComponent<ModularAvatarParameters>().parameters = declared;
                }

                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// VRChat accepts only its own mobile shaders on Android and iOS.
        /// Solid surfaces become Toon Lit and anything transparent becomes
        /// the additive particle shader, each keeping its main texture. The
        /// replacements exist only in this build.
        ///
        /// None of those shaders can discard UV tiles, so whatever a material
        /// hides at rest is cut out of the mesh instead (by Avatar Optimizer,
        /// when it is installed) and stays that way: toggles that work by
        /// showing and hiding tiles do nothing in a mobile build.
        /// </summary>
        private static void MobileMaterials(GameObject avatar)
        {
            var toon = Shader.Find(MobileToon);
            var glow = Shader.Find(MobileGlow);
            if (toon == null || glow == null)
                throw new InvalidOperationException("The VRChat SDK's mobile shaders (" + MobileToon + ", " + MobileGlow + ") are missing.");

            var replaced = new Dictionary<Material, Material>();
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                CutHiddenTiles(renderer, materials);
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || source.shader.name.StartsWith("VRChat/Mobile/", StringComparison.Ordinal)) continue;
                    if (!replaced.TryGetValue(source, out var mobile))
                    {
                        var transparent = source.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        mobile = new Material(transparent ? glow : toon) { name = source.name };
                        if (source.HasProperty("_MainTex"))
                        {
                            mobile.mainTexture = source.GetTexture("_MainTex");
                            mobile.mainTextureScale = source.GetTextureScale("_MainTex");
                            mobile.mainTextureOffset = source.GetTextureOffset("_MainTex");
                        }

                        replaced[source] = mobile;
                    }

                    materials[i] = mobile;
                }

                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// VRChat switches audio sources, lights and colliders off completely
        /// on Android and iOS avatars, and still ranks an avatar that carries
        /// one Very Poor. They are taken out, so a feature loses its sound or
        /// its light there and keeps the rest. A station keeps working
        /// without its collider: VRChat gives a station that has none a
        /// trigger of its own when it loads.
        /// </summary>
        private static void RemoveDisabledComponents(GameObject avatar)
        {
            foreach (var spatial in avatar.GetComponentsInChildren<VRC.SDK3.Avatars.Components.VRCSpatialAudioSource>(true))
                UnityEngine.Object.DestroyImmediate(spatial);
            foreach (var source in avatar.GetComponentsInChildren<AudioSource>(true))
                UnityEngine.Object.DestroyImmediate(source);
            foreach (var light in avatar.GetComponentsInChildren<Light>(true))
                UnityEngine.Object.DestroyImmediate(light);
            foreach (var collider in avatar.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
        }

        private static bool _warnedAboutOptimizer;

        private static void CutHiddenTiles(Renderer renderer, Material[] materials)
        {
            if (!(renderer is SkinnedMeshRenderer)) return;
            var hidden = materials.Select(HiddenTiles).ToList();
            if (hidden.All(tiles => tiles == null)) return;

            // Not public API, so it is added by name and configured as data.
            var type = Type.GetType("Anatawa12.AvatarOptimizer.RemoveMeshByUVTile, com.anatawa12.avatar-optimizer.runtime");
            if (type == null)
            {
                if (!_warnedAboutOptimizer)
                    Debug.LogWarning("[wirenook] Avatar Optimizer is not installed, so UV tiles hidden at rest are left in " +
                                     "the mobile build's meshes and will show.");
                _warnedAboutOptimizer = true;
                return;
            }

            var cutter = renderer.GetComponent(type) ?? renderer.gameObject.AddComponent(type);
            var serialized = new SerializedObject(cutter);
            var slots = serialized.FindProperty("materials");
            slots.arraySize = materials.Length;
            for (var slot = 0; slot < materials.Length; slot++)
            {
                var entry = slots.GetArrayElementAtIndex(slot);
                entry.FindPropertyRelative("uvChannel").enumValueIndex = ToonMaterial.TileUvChannel;
                for (var tile = 0; tile < ToonMaterial.TileColumns * ToonMaterial.TileRows; tile++)
                    entry.FindPropertyRelative("removeTile" + tile).boolValue = hidden[slot] != null && hidden[slot][tile];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Which tiles a material discards as it rests, row by row; null when it discards none.</summary>
        private static bool[] HiddenTiles(Material material)
        {
            if (material == null || !material.HasProperty("_UDIMDiscardCompile") || material.GetFloat("_UDIMDiscardCompile") < 0.5f)
                return null;
            var tiles = new bool[ToonMaterial.TileColumns * ToonMaterial.TileRows];
            for (var row = 0; row < ToonMaterial.TileRows; row++)
            {
                for (var column = 0; column < ToonMaterial.TileColumns; column++)
                {
                    var property = ToonMaterial.TileProperty(column, row);
                    tiles[row * ToonMaterial.TileColumns + column] = material.HasProperty(property) && material.GetFloat(property) > 0.5f;
                }
            }

            return tiles.Any(tile => tile) ? tiles : null;
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
