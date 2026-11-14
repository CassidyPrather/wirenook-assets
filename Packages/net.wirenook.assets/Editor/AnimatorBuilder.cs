using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Rebuilds an animator controller asset from code. The asset keeps its
    /// GUID across rebuilds, so prefabs and Modular Avatar components that
    /// point at it stay wired, and the clips it needs are stored inside it:
    /// one generated file per controller, nothing to keep in sync by hand.
    /// </summary>
    public sealed class ControllerBuilder
    {
        /// <summary>
        /// Applied to every state. One convention avatar-wide keeps Unity from
        /// mixing behaviours, and MMD worlds need Write Defaults on to release
        /// the face when their own animation takes over.
        /// </summary>
        public const bool WriteDefaults = true;

        public AnimatorController Controller { get; }

        private readonly string _path;
        private readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();

        private ControllerBuilder(AnimatorController controller, string path)
        {
            Controller = controller;
            _path = path;
        }

        /// <summary>Opens the controller at <paramref name="assetPath"/> emptied out, creating it if needed.</summary>
        public static ControllerBuilder Rebuild(string assetPath)
        {
            AssetFolders.Ensure(Path.GetDirectoryName(assetPath));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            }

            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset != null && asset != controller) UnityEngine.Object.DestroyImmediate(asset, true);
            }

            return new ControllerBuilder(controller, assetPath);
        }

        public ControllerBuilder Bool(string name, bool defaultValue = false)
        {
            return Parameter(name, AnimatorControllerParameterType.Bool, p => p.defaultBool = defaultValue);
        }

        public ControllerBuilder Int(string name, int defaultValue = 0)
        {
            return Parameter(name, AnimatorControllerParameterType.Int, p => p.defaultInt = defaultValue);
        }

        public ControllerBuilder Float(string name, float defaultValue = 0f)
        {
            return Parameter(name, AnimatorControllerParameterType.Float, p => p.defaultFloat = defaultValue);
        }

        private ControllerBuilder Parameter(string name, AnimatorControllerParameterType type,
            Action<AnimatorControllerParameter> configure)
        {
            if (Controller.parameters.Any(p => p.name == name)) return this;
            var parameter = new AnimatorControllerParameter { name = name, type = type };
            configure(parameter);
            Controller.AddParameter(parameter);
            return this;
        }

        public LayerBuilder Layer(string name, float weight = 1f, AvatarMask mask = null,
            AnimatorLayerBlendingMode blending = AnimatorLayerBlendingMode.Override)
        {
            var machine = new AnimatorStateMachine
            {
                name = name,
                hideFlags = HideFlags.HideInHierarchy,
                anyStatePosition = new Vector3(0, -80),
                entryPosition = new Vector3(0, -160),
                exitPosition = new Vector3(600, -160),
            };
            AssetDatabase.AddObjectToAsset(machine, _path);
            Controller.AddLayer(new AnimatorControllerLayer
            {
                name = name,
                defaultWeight = weight,
                avatarMask = mask,
                blendingMode = blending,
                stateMachine = machine,
            });
            return new LayerBuilder(this, machine);
        }

        /// <summary>
        /// A clip stored inside the controller. Asking for the same name twice
        /// returns the first clip, so layers can share one.
        /// </summary>
        public AnimationClip Clip(string name, Action<ClipBuilder> build = null)
        {
            if (_clips.TryGetValue(name, out var existing)) return existing;
            var clip = new AnimationClip { name = name, hideFlags = HideFlags.None };
            build?.Invoke(new ClipBuilder(clip));
            AssetDatabase.AddObjectToAsset(clip, _path);
            _clips[name] = clip;
            return clip;
        }

        /// <summary>A clip that animates nothing; states need a motion or Unity skips their timing.</summary>
        public AnimationClip Empty => Clip("(empty)", clip => clip.Hold(0.1f));

        public BlendTree Tree(string name, string parameter, params (float threshold, Motion motion)[] children)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, _path);
            foreach (var (threshold, motion) in children) tree.AddChild(motion, threshold);
            return tree;
        }

        internal T AddSubAsset<T>(T asset) where T : UnityEngine.Object
        {
            asset.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(asset, _path);
            return asset;
        }

        public void Save()
        {
            EditorUtility.SetDirty(Controller);
            AssetDatabase.SaveAssetIfDirty(Controller);
        }
    }

    public sealed class LayerBuilder
    {
        public AnimatorStateMachine Machine { get; }

        private readonly ControllerBuilder _owner;
        private int _placed;

        internal LayerBuilder(ControllerBuilder owner, AnimatorStateMachine machine)
        {
            _owner = owner;
            Machine = machine;
        }

        /// <summary>Adds a state. The first one added becomes the layer's default.</summary>
        public StateBuilder State(string name, Motion motion = null)
        {
            // Lay states out in a grid; nobody should have to untangle a generated graph by hand.
            var position = new Vector3(260 + 240 * (_placed % 4), 60 * (_placed / 4));
            _placed++;
            var state = Machine.AddState(name, position);
            state.motion = motion != null ? motion : _owner.Empty;
            state.writeDefaultValues = ControllerBuilder.WriteDefaults;
            return new StateBuilder(_owner, this, state);
        }

        public TransitionBuilder Any(StateBuilder destination)
        {
            return new TransitionBuilder(() =>
            {
                var transition = Machine.AddAnyStateTransition(destination.State);
                transition.canTransitionToSelf = false;
                return transition;
            });
        }
    }

    public sealed class StateBuilder
    {
        public AnimatorState State { get; }

        private readonly ControllerBuilder _owner;
        private readonly LayerBuilder _layer;

        internal StateBuilder(ControllerBuilder owner, LayerBuilder layer, AnimatorState state)
        {
            _owner = owner;
            _layer = layer;
            State = state;
        }

        public StateBuilder Speed(float speed)
        {
            State.speed = speed;
            return this;
        }

        /// <summary>Drives the clip's playhead from a float parameter instead of time.</summary>
        public StateBuilder MotionTime(string parameter)
        {
            State.timeParameterActive = true;
            State.timeParameter = parameter;
            return this;
        }

        public TransitionBuilder To(StateBuilder destination)
        {
            return new TransitionBuilder(() => State.AddTransition(destination.State));
        }

        public TransitionBuilder Exit()
        {
            return new TransitionBuilder(() => State.AddExitTransition());
        }

        public T Behaviour<T>(Action<T> configure = null) where T : StateMachineBehaviour
        {
            var behaviour = State.AddStateMachineBehaviour<T>();
            configure?.Invoke(behaviour);
            EditorUtility.SetDirty(behaviour);
            return behaviour;
        }
    }

    /// <summary>
    /// One logical transition. Conditions added to it are ANDed; <see cref="Or"/>
    /// starts an alternative, which Unity represents as a second transition.
    /// </summary>
    public sealed class TransitionBuilder
    {
        private readonly Func<AnimatorStateTransition> _create;
        private AnimatorStateTransition _current;
        private float _duration;
        private float _exitTime = -1f;

        internal TransitionBuilder(Func<AnimatorStateTransition> create)
        {
            _create = create;
            Start();
        }

        private void Start()
        {
            _current = _create();
            _current.hasExitTime = _exitTime >= 0f;
            _current.exitTime = Mathf.Max(_exitTime, 0f);
            _current.hasFixedDuration = true;
            _current.duration = _duration;
            _current.offset = 0f;
        }

        public TransitionBuilder Fade(float seconds)
        {
            _duration = seconds;
            _current.duration = seconds;
            return this;
        }

        /// <summary>Waits for the source state to reach this normalised time before leaving.</summary>
        public TransitionBuilder AfterTime(float normalizedTime)
        {
            _exitTime = normalizedTime;
            _current.hasExitTime = true;
            _current.exitTime = normalizedTime;
            return this;
        }

        public TransitionBuilder When(string parameter, AnimatorConditionMode mode, float threshold = 0f)
        {
            _current.AddCondition(mode, threshold, parameter);
            return this;
        }

        public TransitionBuilder IfTrue(string parameter) => When(parameter, AnimatorConditionMode.If);
        public TransitionBuilder IfFalse(string parameter) => When(parameter, AnimatorConditionMode.IfNot);
        public TransitionBuilder Equal(string parameter, int value) => When(parameter, AnimatorConditionMode.Equals, value);
        public TransitionBuilder NotEqual(string parameter, int value) => When(parameter, AnimatorConditionMode.NotEqual, value);
        public TransitionBuilder Greater(string parameter, float value) => When(parameter, AnimatorConditionMode.Greater, value);
        public TransitionBuilder Less(string parameter, float value) => When(parameter, AnimatorConditionMode.Less, value);

        public TransitionBuilder Or()
        {
            Start();
            return this;
        }
    }

    public sealed class ClipBuilder
    {
        public AnimationClip Clip { get; }

        internal ClipBuilder(AnimationClip clip)
        {
            Clip = clip;
        }

        public ClipBuilder Loop(bool loop = true)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(Clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(Clip, settings);
            return this;
        }

        /// <summary>Gives an otherwise empty clip a length, by animating a property nothing has.</summary>
        public ClipBuilder Hold(float seconds)
        {
            return Curve("__wirenook_hold", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, seconds, 1f));
        }

        public ClipBuilder Curve(string path, Type type, string property, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(Clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
            return this;
        }

        public ClipBuilder Constant(string path, Type type, string property, float value, float seconds = 1f / 60f)
        {
            return Curve(path, type, property, AnimationCurve.Constant(0f, seconds, value));
        }

        /// <summary>Blendshape weight, 0-100, on the skinned mesh at <paramref name="path"/>.</summary>
        public ClipBuilder Blendshape(string path, string shape, float weight)
        {
            return Constant(path, typeof(SkinnedMeshRenderer), "blendShape." + shape, weight);
        }

        public ClipBuilder Blendshape(string path, string shape, AnimationCurve curve)
        {
            return Curve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape, curve);
        }

        /// <summary>A float material property on the renderer at <paramref name="path"/>.</summary>
        public ClipBuilder Material<TRenderer>(string path, string property, float value) where TRenderer : Renderer
        {
            return Constant(path, typeof(TRenderer), "material." + property, value);
        }

        public ClipBuilder Material<TRenderer>(string path, string property, AnimationCurve curve) where TRenderer : Renderer
        {
            return Curve(path, typeof(TRenderer), "material." + property, curve);
        }

        public ClipBuilder Active(string path, bool active)
        {
            return Constant(path, typeof(GameObject), "m_IsActive", active ? 1f : 0f);
        }

        public ClipBuilder Active(string path, AnimationCurve curve)
        {
            return Curve(path, typeof(GameObject), "m_IsActive", curve);
        }

        /// <summary>Animates an animator parameter (an "AAP"), for values a state must hold or ramp.</summary>
        public ClipBuilder Parameter(string parameter, float value)
        {
            return Constant("", typeof(Animator), parameter, value);
        }

        public ClipBuilder Parameter(string parameter, AnimationCurve curve)
        {
            return Curve("", typeof(Animator), parameter, curve);
        }
    }

    public static class AssetFolders
    {
        /// <summary>Creates every missing folder along an asset path such as <c>Packages/x/Generated/Fx</c>.</summary>
        public static void Ensure(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>
        /// Loads the asset at a path or creates it there, so regenerating keeps
        /// the GUID that other assets already reference.
        /// </summary>
        public static T LoadOrCreate<T>(string assetPath, Func<T> create) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null) return asset;
            Ensure(Path.GetDirectoryName(assetPath));
            asset = create();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }
    }
}
