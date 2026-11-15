using System;
using System.IO;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Shared plumbing for building feature prefabs from code: the drop-in
    /// prefabs that add a gadget to any avatar through Modular Avatar.
    ///
    /// Prefabs are edited in place rather than recreated, so their GUIDs and
    /// internal ids survive a rebuild and anything already placed on an
    /// avatar, including a user's own tweaks to it, keeps working.
    /// </summary>
    public static class FeaturePrefab
    {
        public static void Edit(string path, Action<GameObject> configure)
        {
            if (!File.Exists(path))
            {
                AssetFolders.Ensure(Path.GetDirectoryName(path));
                var seed = new GameObject(Path.GetFileNameWithoutExtension(path));
                PrefabUtility.SaveAsPrefabAsset(seed, path);
                UnityEngine.Object.DestroyImmediate(seed);
            }

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static T Ensure<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        /// <summary>The direct child with this name, created at the parent's origin if missing.</summary>
        public static GameObject Child(GameObject parent, string name)
        {
            var existing = parent.transform.Find(name);
            if (existing != null) return existing.gameObject;
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        /// <summary>
        /// A nested instance of a model under <paramref name="parent"/>. The
        /// instance stays linked to the exported file, so re-exporting from
        /// Blender updates every prefab that contains it.
        /// </summary>
        public static GameObject Model(GameObject parent, string modelPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException("Export the model from Blender first.", modelPath);

            foreach (Transform child in parent.transform)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) != model) continue;

                // The last build left some bones resting at the scale their
                // animator starts them at (see RestAtDefaults). A rebuild
                // measures against the model as it was exported.
                foreach (var part in child.GetComponentsInChildren<Transform>(true))
                {
                    var exported = PrefabUtility.GetCorrespondingObjectFromSource(part);
                    if (exported != null) part.localScale = exported.localScale;
                }

                return child.gameObject;
            }

            return (GameObject)PrefabUtility.InstantiatePrefab(model, parent.transform);
        }

        /// <summary>The transform with this name anywhere below <paramref name="root"/>.</summary>
        public static Transform Find(GameObject root, string name)
        {
            var found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(transform => transform.name == name);
            if (found == null) throw new InvalidOperationException(root.name + " has no object named \"" + name + "\".");
            return found;
        }

        /// <summary>
        /// The bone with this name in a skinned model. Exported props give the
        /// mesh object and its root bone the same name, and only the bone
        /// moves the mesh, so a search by name alone can pick the wrong one.
        /// </summary>
        public static Transform Bone(GameObject model, string name)
        {
            var bone = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(renderer => renderer.bones)
                .FirstOrDefault(candidate => candidate != null && candidate.name == name);
            if (bone == null) throw new InvalidOperationException(model.name + " has no bone named \"" + name + "\".");
            return bone;
        }

        /// <summary>Animation path of <paramref name="target"/> relative to <paramref name="root"/>.</summary>
        public static string PathTo(GameObject root, Transform target)
        {
            return AnimationUtility.CalculateTransformPath(target, root.transform);
        }

        public static string PathTo(GameObject root, string name)
        {
            return PathTo(root, Find(root, name));
        }

        /// <param name="joinAvatarMesh">See <see cref="WirenookFeature.joinAvatarMesh"/>.</param>
        /// <param name="summaryIcon">See <see cref="WirenookFeature.summaryIcon"/>; the feature's own icon when left out.</param>
        /// <param name="pcOnly">The feature cannot work on Android or iOS; see <see cref="WirenookPcOnly"/>.</param>
        public static WirenookFeature Describe(GameObject root, string id, string label, string icon, string tag,
            int tagPriority = 0, bool joinAvatarMesh = false, string summaryIcon = null, bool pcOnly = false)
        {
            var marker = root.GetComponent<WirenookPcOnly>();
            if (pcOnly && marker == null) root.AddComponent<WirenookPcOnly>();
            if (!pcOnly && marker != null) UnityEngine.Object.DestroyImmediate(marker);

            var feature = Ensure<WirenookFeature>(root);
            feature.id = id;
            feature.label = label;
            feature.icon = WirenookIcons.Get(icon);
            feature.summaryIcon = WirenookIcons.Get(summaryIcon ?? icon);
            feature.tag = tag;
            feature.tagPriority = tagPriority;
            feature.joinAvatarMesh = joinAvatarMesh;
            return feature;
        }

        /// <summary>
        /// Merges a controller into one of the avatar's playable layers at
        /// build time. Paths inside it are relative to <paramref name="root"/>,
        /// so the prefab works wherever it is placed in the avatar.
        /// </summary>
        public static void Merge(GameObject root, RuntimeAnimatorController controller,
            VRCAvatarDescriptor.AnimLayerType layer = VRCAvatarDescriptor.AnimLayerType.FX)
        {
            var merge = root.GetComponents<ModularAvatarMergeAnimator>().FirstOrDefault(candidate => candidate.layerType == layer)
                        ?? root.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = layer;
            merge.pathMode = MergeAnimatorPathMode.Relative;
            merge.matchAvatarWriteDefaults = true;
            merge.deleteAttachedAnimator = true;
            if (controller is AnimatorController generated) RestAtDefaults(root, generated);
        }

        /// <summary>
        /// Leaves every transform the controller scales resting at the scale
        /// its layer's default state holds it at, so a prop that is toggled by
        /// scale rests hidden.
        ///
        /// The animator would put it there on the first frame anyway, but not
        /// everyone runs the animator: a viewer whose safety settings block an
        /// avatar's animations sees it exactly as it rests, and so does
        /// anything that renders the prefab without playing it.
        /// </summary>
        public static void RestAtDefaults(GameObject root, AnimatorController controller)
        {
            foreach (var layer in controller.layers)
            {
                var state = layer.stateMachine != null ? layer.stateMachine.defaultState : null;
                if (state == null || !(state.motion is AnimationClip clip)) continue;

                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform) ||
                        !binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)) continue;
                    var target = root.transform.Find(binding.path);
                    if (target == null) continue;

                    var value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
                    var scale = target.localScale;
                    switch (binding.propertyName[binding.propertyName.Length - 1])
                    {
                        case 'x': scale.x = value; break;
                        case 'y': scale.y = value; break;
                        case 'z': scale.z = value; break;
                    }

                    target.localScale = scale;
                }
            }
        }

        /// <summary>Declares the feature's expression parameters, replacing any previous declaration.</summary>
        public static void Parameters(GameObject root, params ParameterConfig[] parameters)
        {
            Ensure<ModularAvatarParameters>(root).parameters = parameters.ToList();
        }

        /// <summary>A switch every viewer sees; costs one synced bit.</summary>
        public static ParameterConfig SyncedBool(string name, bool defaultValue = false, bool saved = false)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = ParameterSyncType.Bool,
                defaultValue = defaultValue ? 1f : 0f,
                hasExplicitDefaultValue = true,
                saved = saved,
            };
        }

        /// <summary>A small synced choice; costs eight synced bits.</summary>
        public static ParameterConfig SyncedInt(string name, int defaultValue = 0, bool saved = false)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = ParameterSyncType.Int,
                defaultValue = defaultValue,
                hasExplicitDefaultValue = true,
                saved = saved,
            };
        }

        /// <summary>Puts the menu items on this object (and its children) into the avatar's menu.</summary>
        public static void InstallMenu(GameObject holder)
        {
            Ensure<ModularAvatarMenuInstaller>(holder);
        }

        public static void MenuToggle(GameObject holder, string label, string icon, string parameter, float value = 1f)
        {
            MenuControl(holder, label, icon, VRCExpressionsMenu.Control.ControlType.Toggle, parameter, value);
        }

        /// <summary>A momentary control: the parameter holds the value only while it is pressed.</summary>
        public static void MenuButton(GameObject holder, string label, string icon, string parameter, float value = 1f)
        {
            MenuControl(holder, label, icon, VRCExpressionsMenu.Control.ControlType.Button, parameter, value);
        }

        /// <summary>A submenu made of the menu items on this object's children.</summary>
        public static void MenuFolder(GameObject holder, string label, string icon)
        {
            var item = MenuControl(holder, label, icon, VRCExpressionsMenu.Control.ControlType.SubMenu, "", 1f);
            item.MenuSource = SubmenuSource.Children;
        }

        private static ModularAvatarMenuItem MenuControl(GameObject holder, string label, string icon,
            VRCExpressionsMenu.Control.ControlType type, string parameter, float value)
        {
            var item = Ensure<ModularAvatarMenuItem>(holder);
            item.label = label;
            item.Control = new VRCExpressionsMenu.Control
            {
                name = label,
                icon = WirenookIcons.Get(icon),
                type = type,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
            };
            // Each item declares its own sync so a feature never depends on
            // how the avatar it lands on configured the same parameter name.
            item.isSynced = true;
            item.isSaved = false;
            return item;
        }

        /// <summary>Moves this object under a humanoid bone at build time, keeping its local pose.</summary>
        public static void AttachToBone(GameObject target, HumanBodyBones bone)
        {
            var proxy = Ensure<ModularAvatarBoneProxy>(target);
            proxy.boneReference = bone;
            proxy.subPath = "";
            proxy.attachmentMode = BoneProxyAttachmentMode.AsChildAtRoot;
        }
    }

    /// <summary>
    /// The house toggle: props pop in with a little overshoot and shrink away,
    /// by scaling a bone rather than switching a renderer. Scaling keeps the
    /// renderer permanently on, which is what lets the build merge every prop
    /// into one mesh; a prop scaled to zero rasterises nothing.
    /// </summary>
    public static class PopToggle
    {
        public const float AppearSeconds = 0.28f;
        public const float DisappearSeconds = 0.18f;

        public sealed class States
        {
            public StateBuilder Off;
            public StateBuilder Appear;
            public StateBuilder On;
            public StateBuilder Disappear;
        }

        /// <summary>
        /// Adds a layer that pops the object at <paramref name="path"/> in
        /// while <paramref name="parameter"/> is true.
        /// </summary>
        /// <param name="extras">
        /// Adds further curves to a state's clip. Called for "off", "appear",
        /// "on" and "disappear"; use it for lights, colliders and effects that
        /// have to follow the prop's visibility.
        /// </param>
        public static States Layer(ControllerBuilder fx, string layerName, string parameter, string path,
            Action<ClipBuilder, string> extras = null)
        {
            fx.Bool(parameter);
            return Layer(fx, layerName, path, on => on.IfTrue(parameter), off => off.IfFalse(parameter), extras);
        }

        /// <summary>
        /// The same layer for a prop that is not switched by one bool of its
        /// own, such as one choice of an integer parameter.
        /// </summary>
        /// <param name="whenOn">Adds the conditions under which the prop is out.</param>
        /// <param name="whenOff">Adds the opposite conditions.</param>
        public static States Layer(ControllerBuilder fx, string layerName, string path,
            Action<TransitionBuilder> whenOn, Action<TransitionBuilder> whenOff, Action<ClipBuilder, string> extras = null)
        {
            var layer = fx.Layer(layerName);

            AnimationClip Clip(string phase, AnimationCurve scale) => fx.Clip(layerName + " " + phase, clip =>
            {
                Scale(clip, path, scale);
                extras?.Invoke(clip, phase);
            });

            var states = new States
            {
                Off = layer.State("Off", Clip("off", AnimationCurve.Constant(0f, 1f / 60f, 0f))),
                Appear = layer.State("Appear", Clip("appear", Overshoot(0f, 1f, AppearSeconds))),
                On = layer.State("On", Clip("on", AnimationCurve.Constant(0f, 1f / 60f, 1f))),
                Disappear = layer.State("Disappear", Clip("disappear", Overshoot(1f, 0f, DisappearSeconds))),
            };

            whenOn(states.Off.To(states.Appear));
            states.Appear.To(states.On).AfterTime(1f);
            whenOff(states.On.To(states.Disappear));
            states.Disappear.To(states.Off).AfterTime(1f);
            return states;
        }

        /// <summary>Uniform scale curves on a transform.</summary>
        public static void Scale(ClipBuilder clip, string path, AnimationCurve curve)
        {
            clip.Curve(path, typeof(Transform), "m_LocalScale.x", curve)
                .Curve(path, typeof(Transform), "m_LocalScale.y", curve)
                .Curve(path, typeof(Transform), "m_LocalScale.z", curve);
        }

        /// <summary>
        /// Eases from one scale to another, swelling past the larger of the
        /// two on the way: the squash-and-stretch that makes a pop read as one.
        /// </summary>
        public static AnimationCurve Overshoot(float from, float to, float seconds)
        {
            var peak = Mathf.Max(from, to) * 1.14f;
            var peakTime = from < to ? seconds * 0.6f : seconds * 0.25f;
            var curve = new AnimationCurve(
                new Keyframe(0f, from),
                new Keyframe(peakTime, peak),
                new Keyframe(seconds, to));
            for (var key = 0; key < curve.length; key++) curve.SmoothTangents(key, 0f);
            return curve;
        }
    }
}
