using UnityEditor;
using UnityEngine;
using Wirenook.Assets.Core;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// The signature colour: one colour the wearer chooses in game, which
    /// the trim of their avatar and everything it carries or sets down then
    /// takes. It is a convention, so that an avatar and a tool from another
    /// package agree on the colour without knowing each other:
    ///
    /// <list type="bullet">
    /// <item>One synced, saved float, <see cref="Parameter"/>: how far the
    /// hue is turned from the painted one, as a share of a full turn
    /// (<see cref="SignatureColour"/>). Eight synced bits. The feature
    /// <see cref="Prefab"/> declares it, puts a dial for it in the menu and
    /// carries the layer that applies it; an avatar wants exactly one.</item>
    /// <item>One material property, <see cref="Property"/>, that any shader
    /// may declare (a Range from 0 to 1 resting at 0): the same amount. The
    /// build gives it to every renderer on the avatar that has a material
    /// with the property, so nothing has to know the renderers by path
    /// (<see cref="WirenookAvatarMaterials"/>).</item>
    /// <item>What follows the colour is painted in
    /// <see cref="SignatureColour"/>'s cyan, or in shades of its hue, and a
    /// shader turns it (Shaders/WirenookSignature.hlsl). A textured surface
    /// says which texels are painted so with a mask, <see cref="Mask"/>, on
    /// Wirenook/lilToon; a shader for something made of light alone can
    /// turn the colour itself.</item>
    /// </list>
    ///
    /// On Android and iOS a material with a mask is drawn with VRChat's
    /// Toon Standard, whose masked hue shift is the same arithmetic, and
    /// the build drives that from the same parameter. A material without
    /// one keeps the painted colour there.
    /// </summary>
    public static class WirenookSignature
    {
        /// <summary>The wearer's choice, 0 to 1: a share of a full turn of the hue. Synced and saved.</summary>
        public const string Parameter = "WN/Signature";

        /// <summary>The material float a shader declares to follow the choice.</summary>
        public const string Property = "_WirenookSignature";

        /// <summary>On Wirenook/lilToon: the texture that is white where the surface is painted in the signature colour.</summary>
        public const string Mask = "_WirenookSignatureMask";

        /// <summary>The feature that puts the choice on an avatar.</summary>
        public const string Prefab = WirenookMaterials.Package + "/Prefabs/Wirenook Signature.prefab";

        private const string Controller = WirenookMaterials.Package + "/Generated/signature-fx.controller";

        public const string FeatureId = "signature";
        public const string MenuLabel = "Colour";
        public const string MenuIcon = "signature";

        /// <summary>The name of the layer that applies the choice; checks look for it.</summary>
        public const string LayerName = "Signature";

        /// <summary>The colour a choice gives, for a preview or for something no shader can turn.</summary>
        public static Color Colour(float signature)
        {
            SignatureColour.Chosen(signature, out var red, out var green, out var blue);
            return new Color(red, green, blue, 1f);
        }

        /// <summary>
        /// Says which texels of a Wirenook/lilToon material are painted in
        /// the signature colour. Where the material glows from its own
        /// colours (<see cref="ToonMaterial.Configure"/> sets that up), the
        /// glow turns with them.
        /// </summary>
        public static void Paint(Material material, Texture2D mask)
        {
            material.SetTexture(Mask, mask);
            material.SetFloat(Property, 0f);
            EditorUtility.SetDirty(material);
        }

        /// <summary>The declaration of the parameter, for a feature that sets the choice.</summary>
        public static nadena.dev.modular_avatar.core.ParameterConfig Declare()
        {
            return FeaturePrefab.SyncedFloat(Parameter, saved: true);
        }

        /// <summary>
        /// Adds the layer that hands the choice to the avatar's materials,
        /// through the stand-in at <paramref name="standInPath"/>
        /// (<see cref="FeaturePrefab.AvatarMaterials"/>). A feature that
        /// needs the amount in its own animator reads the parameter there;
        /// it is declared here as a float.
        /// </summary>
        public static void Layer(ControllerBuilder fx, string standInPath)
        {
            fx.Float(Parameter);
            // One second from no turn to a full one, read at the place the parameter says.
            var turn = fx.Clip("signature", clip =>
                clip.Material<SkinnedMeshRenderer>(standInPath, Property, AnimationCurve.Linear(0f, 0f, 1f, 1f)));
            fx.Layer(LayerName).State("Hue", turn).MotionTime(Parameter);
        }

        [MenuItem("Tools/Wirenook/Assets/Rebuild Signature")]
        public static void Rebuild()
        {
            var fx = ControllerBuilder.Rebuild(Controller);
            // The stand-in is made with the prefab below; its path is its name.
            Layer(fx, StandInName);
            fx.Save();

            FeaturePrefab.Edit(Prefab, root =>
            {
                FeaturePrefab.Describe(root, FeatureId, "Signature colour", MenuIcon, tag: "");
                var standIn = FeaturePrefab.AvatarMaterials(root);
                if (standIn != StandInName)
                    throw new System.InvalidOperationException("The stand-in is at \"" + standIn + "\", not \"" + StandInName + "\".");
                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, Declare());
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuRadial(root, MenuLabel, MenuIcon, Parameter, saved: true);
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[wirenook] Rebuilt the signature colour.");
        }

        private const string StandInName = "Avatar Materials";
    }
}
