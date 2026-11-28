using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// The build steps behind three marks: <see cref="WirenookAvatarRenderers"/>
    /// (a feature animates one stand-in, and every renderer on the avatar
    /// follows), <see cref="WirenookFreeHead"/> (a rig whose humanoid head
    /// bone may be moved) and <see cref="WirenookBoneTarget"/> (a constraint
    /// that moves one of the avatar's bones).
    /// </summary>
    public static class WirenookRenderers
    {
        /// <summary>The property a renderer's first material slot is animated by; the others differ in the number.</summary>
        public const string FirstSlot = "m_Materials.Array.data[0]";

        private static string Slot(int index) => "m_Materials.Array.data[" + index + "]";

        /// <summary>
        /// On a rig marked <see cref="WirenookFreeHead"/>, sends everything
        /// that features attach to the head to the bone that carries the
        /// head people see. Before Modular Avatar moves anything.
        /// </summary>
        public static void HeadProps(GameObject avatar)
        {
            var free = avatar.GetComponent<WirenookFreeHead>();
            if (free == null || free.visual == null) return;

            foreach (var proxy in avatar.GetComponentsInChildren<ModularAvatarBoneProxy>(true))
            {
                if (proxy.boneReference != HumanBodyBones.Head || !string.IsNullOrEmpty(proxy.subPath)) continue;
                proxy.target = free.visual;
                // The proxy remembers the bone it last resolved to.
                proxy.ResolveReferences();
            }
        }

        /// <summary>
        /// Points every constraint marked <see cref="WirenookBoneTarget"/>
        /// at its bone. One that would move the head of a rig not built for
        /// it is removed, object and all.
        /// </summary>
        public static void AimAtBones(GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            var freeHead = avatar.GetComponent<WirenookFreeHead>() != null;

            foreach (var mark in avatar.GetComponentsInChildren<WirenookBoneTarget>(true))
            {
                if (mark == null) continue;
                var bone = animator != null && animator.isHuman ? animator.GetBoneTransform(mark.bone) : null;
                var constraints = mark.GetComponents<VRCConstraintBase>();
                var refused = mark.bone == HumanBodyBones.Head && !freeHead;
                if (bone == null || refused || constraints.Length == 0)
                {
                    Debug.LogWarning("[wirenook] " + mark.name + " moves the avatar's " + mark.bone + " bone, which " +
                                     (refused ? "this rig does not allow: its head bone carries the head (no WirenookFreeHead on the avatar's root)"
                                         : bone == null ? "this avatar does not have" : "needs a VRChat constraint beside the mark") +
                                     ". Left out of the build.");
                    UnityEngine.Object.DestroyImmediate(mark.gameObject);
                    continue;
                }

                foreach (var constraint in constraints) constraint.TargetTransform = bone;
            }
        }

        /// <summary>
        /// Moves what a feature has animated on a
        /// <see cref="WirenookAvatarRenderers"/> stand-in onto every
        /// renderer of the avatar: whether it is enabled, its materials
        /// (every slot), and material properties. Each renderer is given the
        /// same curves, which also keeps them alike for an optimiser that
        /// merges meshes.
        ///
        /// A stand-in whose material swaps are for mobile
        /// (<see cref="WirenookAvatarRenderers.swapsAreForMobile"/>) loses
        /// them in a PC build, and the material properties animated beside
        /// them, which are there to steer the material swapped in. On Android and iOS a material that is
        /// swapped in has to have one of VRChat's mobile shaders, or the
        /// build stops: VRChat would refuse the avatar, or draw it with
        /// nothing.
        /// </summary>
        public static void Spread(GameObject avatar, AnimatorServicesContext animators)
        {
            // The marks are taken off before the build is done; a check that
            // runs afterwards asks here which renderers carried one.
            StandingApart[avatar.GetInstanceID()] = avatar.GetComponentsInChildren<WirenookApart>(true)
                .Select(mark => mark.GetComponent<Renderer>())
                .Where(renderer => renderer != null)
                .ToList();

            var standIns = avatar.GetComponentsInChildren<WirenookAvatarRenderers>(true);
            if (standIns.Length == 0) return;

            // Not the ones that stand apart from the figure: those are their feature's to show and hide.
            var renderers = avatar.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.GetComponent<WirenookApart>() == null)
                .Select(renderer => (renderer, path: animators.ObjectPathRemapper.GetVirtualPathForObject(renderer.gameObject)))
                .ToList();

            if (standIns.Any(standIn => standIn.restHidden))
            {
                foreach (var (renderer, _) in renderers) renderer.enabled = false;
                RestingHidden.Add(avatar.GetInstanceID());
            }

            // Made after the list above was taken, so nothing below switches it off.
            var awake = KeepAwake(avatar, renderers.Select(entry => entry.renderer).ToList());
            if (awake != null) StandingApart[avatar.GetInstanceID()].Add(awake);

            foreach (var standIn in standIns)
            {
                var path = animators.ObjectPathRemapper.GetVirtualPathForObject(standIn.gameObject);
                foreach (var clip in animators.AnimationIndex.GetClipsForObjectPath(path).ToList())
                {
                    foreach (var binding in clip.GetFloatCurveBindings().ToList())
                    {
                        if (binding.path != path || binding.type != typeof(SkinnedMeshRenderer)) continue;
                        var curve = clip.GetFloatCurve(binding);
                        clip.SetFloatCurve(binding, null);
                        // What steers a material that only mobile builds swap in is theirs alone too.
                        if (standIn.swapsAreForMobile && !WirenookPlatform.IsMobile &&
                            binding.propertyName.StartsWith("material.", StringComparison.Ordinal)) continue;
                        foreach (var (renderer, rendererPath) in renderers)
                            clip.SetFloatCurve(rendererPath, renderer.GetType(), binding.propertyName, curve);
                    }

                    foreach (var binding in clip.GetObjectCurveBindings().ToList())
                    {
                        if (binding.path != path || binding.type != typeof(SkinnedMeshRenderer) || binding.propertyName != FirstSlot) continue;
                        var keys = clip.GetObjectCurve(binding);
                        clip.SetObjectCurve(binding, null);
                        if (keys == null) continue;
                        if (WirenookPlatform.IsMobile) Allowed(keys, standIn);
                        else if (standIn.swapsAreForMobile) continue;

                        foreach (var (renderer, rendererPath) in renderers)
                            for (var slot = 0; slot < renderer.sharedMaterials.Length; slot++)
                                clip.SetObjectCurve(rendererPath, renderer.GetType(), Slot(slot), keys);
                    }
                }
            }
        }

        /// <summary>The object of the renderer that is never switched off (<see cref="KeepAwake"/>).</summary>
        public const string AwakeName = "Awake";

        /// <summary>Where the mesh of that renderer is kept.</summary>
        public const string AwakeMeshPath = "Packages/net.wirenook.assets/Generated/awake.asset";

        /// <summary>
        /// The mesh of the renderer that is never switched off: one triangle
        /// a ten-thousandth of its box across, which no pixel is big enough
        /// to show, in bounds that are a unit box. What sees a renderer goes
        /// by its bounds.
        /// </summary>
        public static Mesh AwakeMesh()
        {
            return AssetFolders.LoadOrCreate(AwakeMeshPath, () =>
            {
                var mesh = new Mesh { name = "awake" };
                mesh.vertices = new[] { Vector3.zero, new Vector3(0.0001f, 0f, 0f), new Vector3(0f, 0.0001f, 0f) };
                mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
                mesh.uv = new[] { Vector2.zero, Vector2.zero, Vector2.zero };
                mesh.triangles = new[] { 0, 2, 1 };
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
                return mesh;
            });
        }

        /// <summary>Where the mesh of that renderer is kept for Android and iOS builds.</summary>
        public const string AwakeBareMeshPath = "Packages/net.wirenook.assets/Generated/awake-bare.asset";

        /// <summary>
        /// The same for Android and iOS: the three points and the unit box,
        /// and no triangle at all, so that it has no part to give a material
        /// to. VRChat counts a material slot for each part of a mesh.
        /// </summary>
        public static Mesh AwakeBareMesh()
        {
            return AssetFolders.LoadOrCreate(AwakeBareMeshPath, () =>
            {
                var mesh = new Mesh { name = "awake-bare" };
                mesh.vertices = new[] { Vector3.zero, new Vector3(0.0001f, 0f, 0f), new Vector3(0f, 0.0001f, 0f) };
                mesh.subMeshCount = 0;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
                return mesh;
            });
        }

        /// <summary>
        /// Gives the avatar one renderer that nothing switches off and that
        /// draws nothing, filling the space the figure takes up.
        ///
        /// VRChat does not run the animator of somebody else's avatar while
        /// none of its renderers is in view, and a renderer that is switched
        /// off is never in view. An animator that switches off every
        /// renderer it has therefore stops, and cannot switch them on
        /// again: the avatar stays undrawn on that client, whatever its
        /// wearer does. With this the animator runs whenever the place
        /// where the figure stands is in view.
        /// </summary>
        private static Renderer KeepAwake(GameObject avatar, IReadOnlyList<Renderer> figure)
        {
            var material = figure.SelectMany(renderer => renderer.sharedMaterials).FirstOrDefault(candidate => candidate != null);
            if (material == null) return null;

            var seen = false;
            var space = new Bounds();
            foreach (var renderer in figure)
            {
                var bounds = renderer.bounds;
                if (bounds.size == Vector3.zero) continue;
                if (seen) space.Encapsulate(bounds);
                else space = bounds;
                seen = true;
            }

            if (!seen) space = new Bounds(avatar.transform.position + Vector3.up, new Vector3(1f, 2f, 1f));

            var existing = avatar.transform.Find(AwakeName);
            var holder = existing != null ? existing.gameObject : new GameObject(AwakeName);
            holder.layer = avatar.layer;
            holder.transform.SetParent(avatar.transform, false);
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localPosition = avatar.transform.InverseTransformPoint(space.center);
            var scale = avatar.transform.lossyScale;
            holder.transform.localScale = new Vector3(
                space.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                space.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                space.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));

            var filter = holder.GetComponent<MeshFilter>();
            if (filter == null) filter = holder.AddComponent<MeshFilter>();
            filter.sharedMesh = WirenookPlatform.IsMobile ? AwakeBareMesh() : AwakeMesh();
            var drawn = holder.GetComponent<MeshRenderer>();
            if (drawn == null) drawn = holder.AddComponent<MeshRenderer>();
            // A material slot is what Android and iOS builds have least of: one more takes most
            // of them down a rank. There the renderer has no slot at all; it is still a
            // renderer with bounds, which is what being in view goes by.
            drawn.sharedMaterials = WirenookPlatform.IsMobile ? new Material[0] : new[] { material };
            drawn.enabled = true;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            drawn.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            drawn.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return drawn;
        }

        private static readonly HashSet<int> RestingHidden = new HashSet<int>();
        private static readonly Dictionary<int, List<Renderer>> StandingApart = new Dictionary<int, List<Renderer>>();

        /// <summary>
        /// The renderers the last build of this avatar found marked
        /// <see cref="WirenookApart"/>: not part of the wearer's figure, and
        /// so left alone by whatever hides or redraws the whole avatar.
        /// </summary>
        public static IReadOnlyList<Renderer> Apart(GameObject avatar)
        {
            return StandingApart.TryGetValue(avatar.GetInstanceID(), out var apart)
                ? apart.Where(renderer => renderer != null).ToList()
                : new List<Renderer>();
        }

        /// <summary>
        /// Draws again an avatar that its last build left undrawn at rest
        /// (<see cref="WirenookAvatarRenderers.restHidden"/>), for a picture
        /// of one that the upload leaves undrawn until its animator says who
        /// may see it. True when the avatar was one.
        ///
        /// By now an optimiser may have merged the renderers this build
        /// disabled into one of its own, and switched that off by its
        /// object instead. So everything is switched on: every renderer,
        /// and the object of every renderer that is switched off. A
        /// renderer that rests switched off for a reason of its own shows
        /// in such a picture too.
        /// </summary>
        public static bool DrawAtRest(GameObject avatar)
        {
            if (!RestingHidden.Remove(avatar.GetInstanceID())) return false;
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                if (!renderer.gameObject.activeSelf) renderer.gameObject.SetActive(true);
            }

            return true;
        }

        private static void Allowed(ObjectReferenceKeyframe[] keys, Component standIn)
        {
            foreach (var key in keys)
            {
                if (key.value is Material material && !material.shader.name.StartsWith("VRChat/Mobile/", StringComparison.Ordinal))
                    throw new InvalidOperationException(standIn.name + " swaps in the material " + material.name + " (" + material.shader.name +
                                                        "), which Android and iOS builds may not use.");
            }
        }
    }
}
