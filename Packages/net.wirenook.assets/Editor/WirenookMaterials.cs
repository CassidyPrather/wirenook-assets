using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Generates the materials every wirenook prop shares. A model exported
    /// with a material named <c>wirenook</c> or <c>wirenook-glow</c> is given
    /// the matching asset on import.
    /// </summary>
    public static class WirenookMaterials
    {
        public const string Package = "Packages/net.wirenook.assets";
        public const string Folder = Package + "/Materials";
        public const string PalettePath = Package + "/Textures/wirenook-palette.png";
        public const string EmissionPath = Package + "/Textures/wirenook-palette-emission.png";

        /// <summary>Opaque, toon-shaded; the palette's glow row is emissive.</summary>
        public const string Solid = "wirenook";

        /// <summary>Additive and unshaded, for beams, halos and anything made of light.</summary>
        public const string Glow = "wirenook-glow";

        public static string PathFor(string materialName) => Folder + "/" + materialName + ".mat";

        [MenuItem("Tools/Wirenook/Assets/Rebuild Materials")]
        public static void Rebuild()
        {
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(EmissionPath);
            if (palette == null || emission == null)
                throw new FileNotFoundException("Bake the palette first: tools/palette.py " + Path.ChangeExtension(PalettePath, ".toml"));

            ToonMaterial.EnableClippingCanceller();

            var solid = AssetFolders.LoadOrCreate(PathFor(Solid), () => new Material(Shader.Find(ToonMaterial.ShaderName)));
            ToonMaterial.Configure(solid, palette, emission, doubleSided: false);

            var glow = AssetFolders.LoadOrCreate(PathFor(Glow), () => new Material(Shader.Find(ToonMaterial.ShaderName)));
            ConfigureGlow(glow, palette);

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Light that adds to whatever is behind it. Unlit on purpose: a beam
        /// in a dark room has to stay bright, which is exactly what solid
        /// surfaces must not do.
        /// </summary>
        private static void ConfigureGlow(Material material, Texture2D palette)
        {
            var shader = Shader.Find("Hidden/lilToonTransparent");
            if (shader == null) throw new FileNotFoundException("lilToon's transparent shader is missing; is lilToon installed?");
            material.shader = shader;
            material.SetTexture("_MainTex", palette);
            material.SetColor("_Color", new Color(1f, 1f, 1f, 0.55f));
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_AsUnlit", 1f);
            material.SetFloat("_LightMinLimit", 0f);
            material.SetFloat("_UseShadow", 0f);
            // One-One blending: add, never darken.
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_UDIMDiscardCompile", 1f);
            material.SetFloat("_UDIMDiscardUV", ToonMaterial.TileUvChannel);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }
    }
}
