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
    /// holds the sign's number and, in one number, the layer the shape is on
    /// and how far out the sign stands. A sign is
    /// therefore nothing at all, not even a speck, wherever the shader does
    /// not unfold it: at rest, on a client that blocks the avatar's shaders,
    /// and in a mobile build.
    ///
    /// Signs are numbered from 1. An avatar's material shows one number at
    /// a time in each of three places (<see cref="ToonMaterial.Sign"/>,
    /// <see cref="ToonMaterial.SignB"/>, <see cref="ToonMaterial.SignC"/>),
    /// so features that animate the same one take turns by construction.
    /// Give each sign its own number, whichever of the three shows it.
    /// </summary>
    public sealed class SignMesh
    {
        private int _sign;
        private int _reach;
        private readonly List<Vector2> _places = new List<Vector2>();
        private readonly List<Vector2> _colours = new List<Vector2>();
        private readonly List<Vector2> _which = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        /// <param name="sign">The sign's number: 1 and up, and no other sign's on the same avatar.</param>
        /// <param name="reach">
        /// Metres the sign stands out from its pivot towards the viewer, to
        /// the centimetre. 0.3 from a pivot in the chest stands it at the
        /// edge of the figure; 0 keeps it on the pivot, for one that hangs
        /// in the air beside or above.
        /// </param>
        public SignMesh(int sign, float reach = 0.3f)
        {
            if (sign < 1) throw new ArgumentOutOfRangeException(nameof(sign), "Signs are numbered from 1; 0 is ordinary geometry.");
            if (reach < 0f || reach > 2f) throw new ArgumentOutOfRangeException(nameof(reach), "A sign stands between 0 and 2 m out.");
            _sign = sign;
            _reach = Mathf.RoundToInt(reach * 100f);
        }

        /// <summary>
        /// Starts another sign in the same mesh, on the same pivot: what is
        /// drawn from here on belongs to it. Signs that share a pivot share
        /// a renderer this way.
        /// </summary>
        public SignMesh Next(int sign, float reach = 0.3f)
        {
            if (sign < 1) throw new ArgumentOutOfRangeException(nameof(sign), "Signs are numbered from 1; 0 is ordinary geometry.");
            if (reach < 0f || reach > 2f) throw new ArgumentOutOfRangeException(nameof(reach), "A sign stands between 0 and 2 m out.");
            _sign = sign;
            _reach = Mathf.RoundToInt(reach * 100f);
            return this;
        }

        /// <summary>How many layers a sign can have, back to front.</summary>
        public const int Layers = 16;

        public int Triangles => _triangles.Count / 3;

        /// <summary>A convex shape, corners given anticlockwise as the viewer sees them. A higher layer is drawn in front.</summary>
        public SignMesh Shape(string swatch, int layer, params Vector2[] corners)
        {
            if (corners.Length < 3) throw new ArgumentException("A shape needs three corners.", nameof(corners));
            if (layer < 0 || layer >= Layers) throw new ArgumentOutOfRangeException(nameof(layer), "A sign has layers 0 to " + (Layers - 1) + ".");
            var first = _places.Count;
            var colour = WirenookPalette.Uv(swatch);
            foreach (var corner in corners)
            {
                _places.Add(corner);
                _colours.Add(colour);
                // The number goes in negative: a mesh with no fourth UV set is given
                // a lower set in its place when meshes are merged or drawn, and no
                // other set is negative, so nothing else can pass for a sign. The
                // shader takes the second number apart again: layer + 16 * centimetres.
                _which.Add(new Vector2(-_sign, layer + Layers * _reach));
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

        /// <summary>A star: <paramref name="points"/> spikes between two radii, turned by <paramref name="turn"/> radians.</summary>
        public SignMesh Star(string swatch, int layer, float x, float y, float outer, float inner, int points, float turn = 0f)
        {
            var centre = new Vector2(x, y);
            Vector2 Corner(int i)
            {
                var angle = turn + Mathf.PI * i / points;
                return centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? outer : inner);
            }

            // A star is not convex; a fan of triangles from its centre is.
            for (var i = 0; i < points * 2; i++) Shape(swatch, layer, centre, Corner(i), Corner(i + 1));
            return this;
        }

        /// <summary>A straight bar from one point to another, <paramref name="width"/> thick.</summary>
        public SignMesh Bar(string swatch, int layer, Vector2 from, Vector2 to, float width)
        {
            var along = (to - from).normalized;
            var across = new Vector2(-along.y, along.x) * (width / 2f);
            return Shape(swatch, layer, from - across, to - across, to + across, from + across);
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
