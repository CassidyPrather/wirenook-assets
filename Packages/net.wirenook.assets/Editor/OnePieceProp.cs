using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Rebuilds a prop model as one plain mesh whose parts the material
    /// grows and shrinks, for a prop that is left behind in the world on an
    /// avatar with no material slots to spare.
    ///
    /// <see cref="RigidProp"/> gives each bone's faces a mesh of their own,
    /// which a transform can move, turn and scale; every such mesh is a
    /// material slot. Here the root bone's faces stay as they are and the
    /// faces of up to four named part bones are folded into the same mesh,
    /// each part collapsed to its bone's position, with where every vertex
    /// belongs written beside it for the toon shader to put it back
    /// (<see cref="ToonMaterial.Parts"/>). One mesh, one slot. A part can
    /// then only grow from its point and shrink back to it: it cannot move
    /// or turn. A mobile shader does not know the trick and leaves the
    /// parts folded away, so such a prop is for PC.
    /// </summary>
    public static class OnePieceProp
    {
        public const int MostParts = 4;

        /// <summary>
        /// Builds (or rebuilds in place) the prop under <paramref name="parent"/>.
        /// </summary>
        /// <param name="modelPath">The skinned model; every vertex must be bound to exactly one bone.</param>
        /// <param name="generatedFolder">Where the mesh is stored.</param>
        /// <param name="parts">
        /// Bones whose faces are parts, in order: the first is part 0. Bones
        /// joined by "+" make one part between them, each growing from its
        /// own point.
        /// </param>
        /// <returns>The object that carries the mesh, named after the model's root bone.</returns>
        public static GameObject Build(GameObject parent, string modelPath, string generatedFolder, params string[] parts)
        {
            if (parts.Length > MostParts)
                throw new ArgumentException("A one-piece prop has at most " + MostParts + " parts; " + modelPath + " was given " + parts.Length + ".");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException("Export the model from Blender first.", modelPath);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin == null) throw new InvalidDataException(modelPath + " has no skinned mesh.");

            var source = skin.sharedMesh;
            var bones = skin.bones;
            var name = Path.GetFileNameWithoutExtension(modelPath);
            var rootIndex = Array.FindIndex(bones, bone => !bones.Contains(bone.parent));
            if (rootIndex < 0) throw new InvalidDataException(modelPath + " has no root bone.");
            var root = bones[rootIndex];

            var partOf = new int[bones.Length];
            for (var bone = 0; bone < bones.Length; bone++)
            {
                partOf[bone] = Array.FindIndex(parts, part => part.Split('+').Contains(bones[bone].name));
                if (bone != rootIndex && partOf[bone] < 0)
                    throw new InvalidDataException(modelPath + ": the bone " + bones[bone].name + " is neither the root nor one of the parts asked for.");
            }

            foreach (var part in parts.SelectMany(part => part.Split('+')))
            {
                if (bones.All(bone => bone.name != part)) throw new InvalidDataException(modelPath + " has no bone named " + part + ".");
            }

            var container = FeaturePrefab.Child(parent, name);
            var holder = FeaturePrefab.Child(container, root.name);
            var placed = model.transform.worldToLocalMatrix * root.localToWorldMatrix;
            holder.transform.localPosition = placed.GetColumn(3);
            holder.transform.localRotation = placed.rotation;
            holder.transform.localScale = placed.lossyScale;
            // What an earlier build as a rigid prop left behind.
            foreach (var part in parts.SelectMany(part => part.Split('+')))
            {
                var stale = holder.transform.Find(part);
                if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }

            // The bind pose takes mesh space into a bone's own space; the root's is the space of the new mesh.
            var toRoot = source.bindposes[rootIndex];
            var weights = source.boneWeights;
            var vertices = source.vertices.Select(vertex => toRoot.MultiplyPoint3x4(vertex)).ToArray();
            var normals = source.normals.Select(normal => toRoot.MultiplyVector(normal).normalized).ToArray();
            var stored = new Vector3[vertices.Length];
            var places = new Vector2[vertices.Length];
            var which = new Vector2[vertices.Length];
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (var vertex = 0; vertex < vertices.Length; vertex++)
            {
                var weight = weights[vertex];
                if (weight.weight0 < 0.999f)
                    throw new InvalidDataException(modelPath + ": a vertex is blended between bones. Props need every vertex bound to one bone.");
                bounds.Encapsulate(vertices[vertex]);
                var part = partOf[weight.boneIndex0];
                if (part < 0)
                {
                    stored[vertex] = vertices[vertex];
                    continue;
                }

                var point = root.InverseTransformPoint(bones[weight.boneIndex0].position);
                var offset = vertices[vertex] - point;
                stored[vertex] = point;
                places[vertex] = new Vector2(offset.x, offset.y);
                // Negative, both: no mesh that merely lacks these sets is ever handed that.
                which[vertex] = new Vector2(-(part + 1), -(offset.z + 16f));
            }

            var mesh = AssetFolders.LoadOrCreate(generatedFolder + "/" + name + " in one piece.asset", () => new Mesh());
            mesh.Clear();
            mesh.name = name + " in one piece";
            mesh.SetVertices(stored);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, source.uv);
            mesh.SetUVs(ToonMaterial.ScreenUvChannel, places);
            mesh.SetUVs(ToonMaterial.ScreenUvChannel + 1, which);
            mesh.subMeshCount = source.subMeshCount;
            for (var submesh = 0; submesh < source.subMeshCount; submesh++) mesh.SetTriangles(source.GetTriangles(submesh), submesh);
            // The folded parts are not where they will be drawn; the bounds have to cover them unfolded.
            mesh.bounds = bounds;
            EditorUtility.SetDirty(mesh);

            FeaturePrefab.Ensure<MeshFilter>(holder).sharedMesh = mesh;
            FeaturePrefab.Ensure<MeshRenderer>(holder).sharedMaterials = skin.sharedMaterials;
            return holder;
        }

        /// <summary>The animated property that says how far a part has grown: 0 folded away, 1 whole.</summary>
        public static string Shown(int part)
        {
            if (part < 0 || part >= MostParts) throw new ArgumentOutOfRangeException(nameof(part));
            return "material." + ToonMaterial.Parts + "." + "xyzw"[part];
        }

        /// <summary>Adds the parts' lean to a clip (see <see cref="ToonMaterial.PartsSway"/>).</summary>
        public static ClipBuilder Sway(ClipBuilder clip, string path, AnimationCurve curve)
        {
            return clip.Curve(path, typeof(MeshRenderer), "material." + ToonMaterial.PartsSway, curve);
        }

        /// <summary>Adds a part's size to a clip. <paramref name="path"/> is the object <see cref="Build"/> returned.</summary>
        public static ClipBuilder Show(ClipBuilder clip, string path, int part, AnimationCurve curve)
        {
            return clip.Curve(path, typeof(MeshRenderer), Shown(part), curve);
        }
    }
}
