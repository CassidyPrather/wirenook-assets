using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>The palette's swatches by name, as places on the palette texture.</summary>
    public static class WirenookPalette
    {
        private const string JsonPath = WirenookMaterials.Package + "/Textures/wirenook-palette.json";
        private static Dictionary<string, Vector2> _uvs;

        /// <summary>The centre of a swatch on the main UV set.</summary>
        public static Vector2 Uv(string swatch)
        {
            if (_uvs == null)
            {
                _uvs = new Dictionary<string, Vector2>();
                var json = JObject.Parse(File.ReadAllText(JsonPath));
                foreach (var entry in (JObject)json["swatches"])
                    _uvs[entry.Key] = new Vector2((float)entry.Value["uv"][0], (float)entry.Value["uv"][1]);
            }

            if (!_uvs.TryGetValue(swatch, out var uv))
                throw new KeyNotFoundException("The palette has no swatch named \"" + swatch + "\" (" + JsonPath + ").");
            return uv;
        }
    }

    /// <summary>
    /// Builds a sign: flat artwork that the toon shader (Shaders/Toon) turns
    /// to face whoever looks at it and stands a little way out from the
    /// avatar towards them. Drawn in palette colours, so it wears the shared
    /// <c>wirenook</c> material and, merged into the avatar's mesh at build
    /// time, costs no mesh and no material slot of its own.
    ///
    /// Every vertex is stored at the pivot. Where it belongs on the sign is
    /// in the third UV set, in metres right and up of the pivot; the fourth
    /// holds the sign's number and the layer the shape is on. A sign is
    /// therefore nothing at all, not even a speck, wherever the shader does
    /// not unfold it: at rest, on a client that blocks the avatar's shaders,
    /// and in a mobile build.
    ///
    /// Signs are numbered from 1, and an avatar's material shows one number
    /// at a time (<see cref="ToonMaterial.Sign"/>). Features that each bring
    /// a sign take turns by construction; give each its own number.
    /// </summary>
    public sealed class SignMesh
    {
        private readonly int _sign;
        private readonly List<Vector2> _places = new List<Vector2>();
        private readonly List<Vector2> _colours = new List<Vector2>();
        private readonly List<Vector2> _which = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        public SignMesh(int sign)
        {
            if (sign < 1) throw new ArgumentOutOfRangeException(nameof(sign), "Signs are numbered from 1; 0 is ordinary geometry.");
            _sign = sign;
        }

        public int Triangles => _triangles.Count / 3;

        /// <summary>A convex shape, corners given anticlockwise as the viewer sees them. A higher layer is drawn in front.</summary>
        public SignMesh Shape(string swatch, int layer, params Vector2[] corners)
        {
            if (corners.Length < 3) throw new ArgumentException("A shape needs three corners.", nameof(corners));
            var first = _places.Count;
            var colour = WirenookPalette.Uv(swatch);
            foreach (var corner in corners)
            {
                _places.Add(corner);
                _colours.Add(colour);
                _which.Add(new Vector2(_sign, layer));
            }

            for (var i = 1; i < corners.Length - 1; i++)
            {
                // Unity draws a triangle whose corners run clockwise on screen as its front.
                _triangles.Add(first);
                _triangles.Add(first + i + 1);
                _triangles.Add(first + i);
            }

            return this;
        }

        /// <summary>A rectangle by its centre and size, in metres.</summary>
        public SignMesh Box(string swatch, int layer, float x, float y, float width, float height)
        {
            var w = width / 2f;
            var h = height / 2f;
            return Shape(swatch, layer, new Vector2(x - w, y - h), new Vector2(x + w, y - h), new Vector2(x + w, y + h), new Vector2(x - w, y + h));
        }

        /// <summary>A disc, as a polygon of <paramref name="sides"/>.</summary>
        public SignMesh Disc(string swatch, int layer, float x, float y, float radius, int sides = 12)
        {
            return Shape(swatch, layer, Enumerable.Range(0, sides)
                .Select(i => new Vector2(x + radius * Mathf.Cos(2f * Mathf.PI * i / sides), y + radius * Mathf.Sin(2f * Mathf.PI * i / sides))).ToArray());
        }

        /// <summary>
        /// Block capitals from a five by seven pixel face, each run of lit
        /// pixels in a row one rectangle. <paramref name="x"/> and
        /// <paramref name="y"/> are the centre of the whole line.
        /// </summary>
        /// <param name="pixel">The size of one pixel, in metres.</param>
        public SignMesh Text(string swatch, int layer, string text, float x, float y, float pixel)
        {
            const int columns = 5, rows = 7, gap = 1;
            var width = (text.Length * (columns + gap) - gap) * pixel;
            var left = x - width / 2f;
            var top = y + rows * pixel / 2f;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == ' ') continue;
                if (!Glyphs.TryGetValue(text[index], out var glyph))
                    throw new ArgumentException("The sign face has no glyph for '" + text[index] + "'.", nameof(text));
                var origin = left + index * (columns + gap) * pixel;
                for (var row = 0; row < rows; row++)
                {
                    var column = 0;
                    while (column < columns)
                    {
                        if (glyph[row][column] != '#')
                        {
                            column++;
                            continue;
                        }

                        var start = column;
                        while (column < columns && glyph[row][column] == '#') column++;
                        var runWidth = (column - start) * pixel;
                        Box(swatch, layer, origin + start * pixel + runWidth / 2f, top - (row + 0.5f) * pixel, runWidth, pixel);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Writes the mesh asset in place (its GUID is kept), skinned to one
        /// bone at its origin: the pivot the sign stands out from.
        /// </summary>
        public Mesh Write(string assetPath)
        {
            var mesh = AssetFolders.LoadOrCreate(assetPath, () => new Mesh());
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(assetPath);
            var count = _places.Count;
            mesh.SetVertices(Enumerable.Repeat(Vector3.zero, count).ToList());
            // Towards the front of the avatar: what the surface is lit by where the shader is not ours.
            mesh.SetNormals(Enumerable.Repeat(Vector3.forward, count).ToList());
            mesh.SetUVs(0, _colours);
            // Tile (0,0) of the discard set: never hidden by an outfit switch.
            mesh.SetUVs(1, Enumerable.Repeat(new Vector2(0.5f, 0.5f), count).ToList());
            mesh.SetUVs(2, _places);
            mesh.SetUVs(3, _which);
            mesh.SetTriangles(_triangles, 0);
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, count).ToArray();
            mesh.bindposes = new[] { Matrix4x4.identity };
            // The avatar's body gives a joined prop its own bounds; on its own
            // the sign needs room to stand out and unfold in.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.2f);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// The sign as a skinned renderer under <paramref name="parent"/>,
        /// wearing the shared solid material. Mark the feature
        /// <c>joinAvatarMesh</c> and the build merges it into the avatar.
        /// </summary>
        public GameObject Place(GameObject parent, string name, string meshAssetPath)
        {
            var mesh = Write(meshAssetPath);
            var holder = FeaturePrefab.Child(parent, name);
            var renderer = FeaturePrefab.Ensure<SkinnedMeshRenderer>(holder);
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { holder.transform };
            renderer.rootBone = holder.transform;
            renderer.localBounds = mesh.bounds;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(WirenookMaterials.PathFor(WirenookMaterials.Solid));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return holder;
        }

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
            { 'C', new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" } },
            { 'D', new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." } },
            { 'E', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" } },
            { 'F', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." } },
            { 'G', new[] { ".####", "#....", "#....", "#.###", "#...#", "#...#", ".###." } },
            { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'I', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" } },
            { 'J', new[] { "..###", "....#", "....#", "....#", "....#", "#...#", ".###." } },
            { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
            { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
            { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
            { 'N', new[] { "#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#" } },
            { 'O', new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
            { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
            { 'Q', new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" } },
            { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
            { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
            { 'T', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." } },
            { 'U', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
            { 'V', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." } },
            { 'W', new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" } },
            { 'X', new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" } },
            { 'Y', new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." } },
            { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
            { '0', new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." } },
            { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
            { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
            { '3', new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." } },
            { '4', new[] { "#...#", "#...#", "#...#", "#####", "....#", "....#", "....#" } },
            { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
            { '6', new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." } },
            { '7', new[] { "#####", "....#", "...#.", "..#..", "..#..", "..#..", "..#.." } },
            { '8', new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." } },
            { '9', new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." } },
            { '!', new[] { "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.." } },
            { '?', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." } },
            { '.', new[] { ".....", ".....", ".....", ".....", ".....", ".....", "..#.." } },
            { ':', new[] { ".....", "..#..", ".....", ".....", ".....", "..#..", "....." } },
            { '(', new[] { "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#." } },
            { ')', new[] { ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..." } },
            { '-', new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." } },
            { 'x', new[] { ".....", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "....." } },
        };
    }
}
