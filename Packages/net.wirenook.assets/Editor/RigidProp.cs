using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Rebuilds a prop model as plain meshes, one per bone, for props that
    /// are left behind in the world.
    ///
    /// Props are exported skinned so that hand-held ones can be merged into
    /// the avatar's mesh. A prop that is dropped in the world must not be: a
    /// merged mesh is culled as one, by the avatar's bounds, so the prop would
    /// disappear whenever the avatar itself went out of view. As a separate
    /// skinned mesh it would count against the tightest limit VRChat ranks
    /// avatars on. Rigid props need no skinning at all, so each bone's faces
    /// become an ordinary mesh on an object of the same name, which is culled
    /// by its own bounds and animated by its own transform.
    /// </summary>
    public static class RigidProp
    {
        /// <summary>
        /// Builds (or rebuilds in place) the prop under <paramref name="parent"/>.
        /// </summary>
        /// <param name="modelPath">The skinned model; every vertex must be bound to exactly one bone.</param>
        /// <param name="generatedFolder">Where the split meshes are stored.</param>
        /// <returns>The prop's container; its descendants are named after the model's bones.</returns>
        public static GameObject Build(GameObject parent, string modelPath, string generatedFolder)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException("Export the model from Blender first.", modelPath);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin == null) throw new InvalidDataException(modelPath + " has no skinned mesh.");

            // A prefab built before this prop was made rigid still holds the
            // skinned model itself; take it out so it is not drawn twice.
            foreach (var child in parent.transform.Cast<Transform>().ToList())
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == model)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            var source = skin.sharedMesh;
            var bones = skin.bones;
            var name = Path.GetFileNameWithoutExtension(modelPath);
            var container = FeaturePrefab.Child(parent, name);

            // Mirror the bone hierarchy, parents before children.
            var objects = new Dictionary<Transform, GameObject>();
            foreach (var bone in bones.OrderBy(Depth))
            {
                var holder = bone.parent != null && objects.TryGetValue(bone.parent, out var parentObject) ? parentObject : container;
                var copy = FeaturePrefab.Child(holder, bone.name);
                if (holder == container)
                {
                    // Root bones keep their pose in the model's own space.
                    var matrix = model.transform.worldToLocalMatrix * bone.localToWorldMatrix;
                    copy.transform.localPosition = matrix.GetColumn(3);
                    copy.transform.localRotation = matrix.rotation;
                    copy.transform.localScale = matrix.lossyScale;
                }
                else
                {
                    copy.transform.localPosition = bone.localPosition;
                    copy.transform.localRotation = bone.localRotation;
                    copy.transform.localScale = bone.localScale;
                }

                objects[bone] = copy;
            }

            var weights = source.boneWeights;
            var vertices = source.vertices;
            var normals = source.normals;
            var uvs = source.uv;
            var bindPoses = source.bindposes;

            for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                var holder = objects[bones[boneIndex]];
                var remap = new Dictionary<int, int>();
                var partVertices = new List<Vector3>();
                var partNormals = new List<Vector3>();
                var partUvs = new List<Vector2>();
                var partTriangles = new List<List<int>>();
                var partMaterials = new List<Material>();

                for (var submesh = 0; submesh < source.subMeshCount; submesh++)
                {
                    var triangles = source.GetTriangles(submesh);
                    var kept = new List<int>();
                    for (var i = 0; i < triangles.Length; i += 3)
                    {
                        var owners = new[] { Owner(weights[triangles[i]]), Owner(weights[triangles[i + 1]]), Owner(weights[triangles[i + 2]]) };
                        if (owners.Any(owner => owner < 0) || owners[0] != owners[1] || owners[1] != owners[2])
                        {
                            throw new InvalidDataException(
                                modelPath + ": a face is shared between bones or blended. Rigid props need every face bound to one bone.");
                        }

                        if (owners[0] != boneIndex) continue;
                        for (var corner = 0; corner < 3; corner++)
                        {
                            var vertex = triangles[i + corner];
                            if (!remap.TryGetValue(vertex, out var mapped))
                            {
                                mapped = partVertices.Count;
                                remap[vertex] = mapped;
                                // The bind pose takes mesh space into the bone's own space.
                                partVertices.Add(bindPoses[boneIndex].MultiplyPoint3x4(vertices[vertex]));
                                partNormals.Add(bindPoses[boneIndex].MultiplyVector(normals[vertex]).normalized);
                                partUvs.Add(uvs[vertex]);
                            }

                            kept.Add(mapped);
                        }
                    }

                    if (kept.Count == 0) continue;
                    partTriangles.Add(kept);
                    partMaterials.Add(skin.sharedMaterials[submesh]);
                }

                if (partTriangles.Count == 0)
                {
                    // A bone that only positions others carries no mesh.
                    foreach (var stale in holder.GetComponents<MeshRenderer>()) UnityEngine.Object.DestroyImmediate(stale);
                    foreach (var stale in holder.GetComponents<MeshFilter>()) UnityEngine.Object.DestroyImmediate(stale);
                    continue;
                }

                var mesh = AssetFolders.LoadOrCreate(generatedFolder + "/" + name + " " + bones[boneIndex].name + ".asset", () => new Mesh());
                mesh.Clear();
                mesh.name = name + " " + bones[boneIndex].name;
                mesh.SetVertices(partVertices);
                mesh.SetNormals(partNormals);
                mesh.SetUVs(0, partUvs);
                mesh.subMeshCount = partTriangles.Count;
                for (var submesh = 0; submesh < partTriangles.Count; submesh++) mesh.SetTriangles(partTriangles[submesh], submesh);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);

                FeaturePrefab.Ensure<MeshFilter>(holder).sharedMesh = mesh;
                var renderer = FeaturePrefab.Ensure<MeshRenderer>(holder);
                renderer.sharedMaterials = partMaterials.ToArray();
            }

            return container;
        }

        /// <summary>The single bone a vertex is fully bound to, or -1 if it is blended.</summary>
        private static int Owner(BoneWeight weight)
        {
            return weight.weight0 > 0.999f ? weight.boneIndex0 : -1;
        }

        private static int Depth(Transform transform)
        {
            var depth = 0;
            for (var parent = transform.parent; parent != null; parent = parent.parent) depth++;
            return depth;
        }
    }
}
