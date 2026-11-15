using System;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Import settings for the textures, models and sounds every wirenook
    /// package ships, applied by file name so they hold in whichever project
    /// imports the package rather than depending on per-file settings
    /// staying intact.
    /// </summary>
    public sealed class WirenookImportRules : AssetPostprocessor
    {
        /// <summary>The largest icon VRChat accepts in an expression menu.</summary>
        public const int MenuIconSize = 256;

        /// <summary>Bump to make Unity re-import everything these rules cover after they change.</summary>
        public override uint GetVersion() => 3;

        private void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;
            var path = assetPath.Replace('\\', '/');

            if (IsPalette(path))
            {
                // Swatch cells are solid, so mip levels and compression can
                // only blur one cell into the next; at this size neither saves
                // anything worth that.
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.sRGBTexture = !path.EndsWith("-emission.png", StringComparison.Ordinal);
            }
            else if (path.Contains("/net.wirenook.") && path.Contains("/Textures/"))
            {
                // VRChat requires mip streaming on every texture that has mip
                // levels, and warns on upload about any that lack it.
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
            }
            else if (IsMenuIcon(path))
            {
                importer.maxTextureSize = MenuIconSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
            }
        }

        private void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/net.wirenook.") || !path.Contains("/Sounds/")) return;

            // The SDK refuses to upload an avatar whose animator plays a clip
            // that is decompressed on load unless it loads in the background,
            // so that a short sound effect cannot stall whoever loads the avatar.
            ((AudioImporter)assetImporter).loadInBackground = true;
        }

        private bool IsPropModel => assetPath.Replace('\\', '/').Contains("/net.wirenook.") &&
                                    assetPath.Replace('\\', '/').Contains("/Models/");

        private void OnPreprocessModel()
        {
            if (!IsPropModel) return;
            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importAnimation = false;
            // Props are exported Y-up with identity transforms; Unity's axis
            // baking would only turn them to face backwards.
            importer.bakeAxisConversion = false;
            // VRChat cannot rank an avatar that carries an unreadable mesh.
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importBlendShapeNormals = ModelImporterNormals.None;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsPropModel) return;

            // Props name their materials after the shared ones; pointing every
            // prop at the same material assets is what lets a whole set of
            // gadgets collapse into one or two material slots at build time.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    var path = WirenookMaterials.PathFor(materials[i].name);
                    context.DependsOnArtifact(path);
                    var shared = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (shared != null) materials[i] = shared;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static bool IsPalette(string path)
        {
            return path.EndsWith("-palette.png", StringComparison.Ordinal) ||
                   path.EndsWith("-palette-emission.png", StringComparison.Ordinal);
        }

        private static bool IsMenuIcon(string path)
        {
            return path.Contains("/net.wirenook.") && path.Contains("/Icons/") &&
                   path.EndsWith(".png", StringComparison.Ordinal);
        }
    }

    public static class WirenookIcons
    {
        public const string Folder = "Packages/net.wirenook.assets/Icons";

        /// <summary>The shared menu icon with this name, or null when the set has none.</summary>
        public static Texture2D Get(string name)
        {
            return string.IsNullOrEmpty(name)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + name + ".png");
        }
    }
}
