using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// The one way wirenook materials are set up, so every prop and avatar
    /// part starts from the same lilToon configuration. Parts that share a
    /// configuration and a palette can share a material slot once they are
    /// merged at build time.
    /// </summary>
    public static class ToonMaterial
    {
        public const string ShaderName = "lilToon";

        /// <summary>The UV set that carries discard tiles; UV0 stays free for texturing.</summary>
        public const int TileUvChannel = 1;

        public const int TileColumns = 4;
        public const int TileRows = 4;

        /// <summary>The material float that hides one discard tile when set to 1.</summary>
        public static string TileProperty(int column, int row)
        {
            if (column < 0 || column >= TileColumns || row < 0 || row >= TileRows)
                throw new ArgumentOutOfRangeException(nameof(column), "tile (" + column + "," + row + ") is outside the 4x4 grid");
            return "_UDIMDiscardRow" + row + "_" + column;
        }

        /// <summary>
        /// Configures <paramref name="material"/> as a flat toon surface reading
        /// its colours from a palette texture.
        /// </summary>
        /// <param name="emissionMask">White where the palette glows; null for no emission.</param>
        /// <param name="doubleSided">Render back faces too, for single-sheet geometry such as hair cards.</param>
        public static void Configure(Material material, Texture2D palette, Texture2D emissionMask, bool doubleSided)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("lilToon is not installed (shader \"" + ShaderName + "\" not found).");
            material.shader = shader;

            material.SetTexture("_MainTex", palette);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Cull", doubleSided ? 0f : 2f);

            // No brightness floor: in a dark world the avatar should be dark,
            // not a glowing cut-out. lilToon defaults this to 0.05.
            material.SetFloat("_LightMinLimit", 0f);

            material.SetFloat("_UseShadow", 1f);
            material.SetColor("_ShadowColor", new Color(0.80f, 0.74f, 0.88f, 1f));
            material.SetFloat("_ShadowBorder", 0.5f);
            material.SetFloat("_ShadowBlur", 0.08f);
            // Shadows cast by world geometry band badly across flat shading.
            material.SetFloat("_ShadowReceive", 0f);

            material.SetFloat("_UDIMDiscardCompile", 1f);
            material.SetFloat("_UDIMDiscardUV", TileUvChannel);
            // Vertex mode collapses whole triangles before rasterising, so a
            // hidden part costs no fill; it needs every face to sit in one tile.
            material.SetFloat("_UDIMDiscardMode", 0f);

            var glows = emissionMask != null;
            material.SetFloat("_UseEmission", glows ? 1f : 0f);
            if (glows)
            {
                material.SetTexture("_EmissionMap", emissionMask);
                material.SetColor("_EmissionColor", Color.white);
                // Glow in the swatch's own colour rather than a flat tint.
                material.SetFloat("_EmissionMainStrength", 1f);
            }

            EditorUtility.SetDirty(material);
        }

        /// <summary>Sets a tile's resting visibility on the material asset itself.</summary>
        public static void SetTileShown(Material material, int column, int row, bool shown)
        {
            material.SetFloat(TileProperty(column, row), shown ? 0f : 1f);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Turns on lilToon's Clipping Canceller project-wide: geometry that
        /// would be sliced by the camera's near plane is pushed back onto it
        /// instead, so a friend leaning in sees a face rather than its inside.
        /// It is a shader setting stored per project, not per material, so
        /// every project that uses these materials has to enable it once.
        /// </summary>
        /// <returns>True when the setting changed.</returns>
        public static bool EnableClippingCanceller()
        {
            // lilToon keeps this API internal, so reach it by name.
            var type = Type.GetType("lilToonSetting, lilToon.Editor");
            if (type == null) throw new InvalidOperationException("lilToon's editor assembly is not loaded.");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            object setting = null;
            var arguments = new[] { setting };
            type.GetMethod("InitializeShaderSetting", flags).Invoke(null, arguments);
            setting = arguments[0];

            var field = type.GetField("LIL_FEATURE_CLIPPING_CANCELLER");
            if ((bool)field.GetValue(setting)) return false;
            field.SetValue(setting, true);

            type.GetMethod("SaveShaderSetting", flags).Invoke(null, new[] { setting });
            var apply = type.GetMethod("ApplyShaderSetting", flags, null, new[] { type, typeof(string) }, null);
            apply.Invoke(null, new[] { setting, null });
            return true;
        }
    }
}
