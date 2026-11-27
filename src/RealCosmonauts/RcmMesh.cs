using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RealCosmonauts
{
    /// <summary>
    /// A mesh in the Real Cosmonauts mesh format (.rcm), written by art/tools/make_heads.py.
    ///
    /// Layout (little-endian):
    ///   "RCM1"
    ///   int boneCount, then boneCount x (int byteLength, UTF-8 bone name)
    ///   int vertexCount, then per vertex:
    ///     float3 position, float3 normal, float2 uv, 4 x (int boneIndex, float weight)
    ///   int subMeshCount, then per submesh: int indexCount, indexCount x int
    ///
    /// Positions and normals use Unity axes and are relative to the head pivot
    /// (the top of the neck), in metres at real human scale.
    /// </summary>
    internal class RcmMesh
    {
        public string[] BoneNames;
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[,] BoneIndices;
        public float[,] BoneWeights;
        public int[][] SubMeshes;

        public static RcmMesh Load(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (magic != "RCM1")
                    throw new InvalidDataException($"{path} is not an RCM1 file");

                var mesh = new RcmMesh();
                mesh.BoneNames = new string[reader.ReadInt32()];
                for (int i = 0; i < mesh.BoneNames.Length; i++)
                    mesh.BoneNames[i] = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));

                int count = reader.ReadInt32();
                mesh.Positions = new Vector3[count];
                mesh.Normals = new Vector3[count];
                mesh.Uvs = new Vector2[count];
                mesh.BoneIndices = new int[count, 4];
                mesh.BoneWeights = new float[count, 4];
                for (int i = 0; i < count; i++)
                {
                    mesh.Positions[i] = ReadVector3(reader);
                    mesh.Normals[i] = ReadVector3(reader);
                    mesh.Uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    for (int k = 0; k < 4; k++)
                    {
                        mesh.BoneIndices[i, k] = reader.ReadInt32();
                        mesh.BoneWeights[i, k] = reader.ReadSingle();
                    }
                }

                mesh.SubMeshes = new int[reader.ReadInt32()][];
                for (int s = 0; s < mesh.SubMeshes.Length; s++)
                {
                    var indices = new int[reader.ReadInt32()];
                    for (int i = 0; i < indices.Length; i++)
                        indices[i] = reader.ReadInt32();
                    mesh.SubMeshes[s] = indices;
                }
                return mesh;
            }
        }

        /// <summary>
        /// Builds a Unity mesh skinned to the given bones. Bones are matched by name; a bone
        /// the target skeleton lacks falls back to <paramref name="fallbackBone"/>.
        /// Vertices are scaled by <paramref name="scale"/> and placed at <paramref name="pivot"/>.
        /// </summary>
        public Mesh ToUnityMesh(string name, Transform[] bones, Matrix4x4[] bindposes, int fallbackBone, Vector3 pivot, float scale)
        {
            var remap = new int[BoneNames.Length];
            for (int b = 0; b < BoneNames.Length; b++)
            {
                remap[b] = Array.FindIndex(bones, t => t != null && t.name == BoneNames[b]);
                if (remap[b] < 0)
                {
                    Log.Warning($"{name}: bone {BoneNames[b]} not found, using {bones[fallbackBone].name}");
                    remap[b] = fallbackBone;
                }
            }

            int count = Positions.Length;
            var vertices = new Vector3[count];
            var weights = new BoneWeight[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = pivot + Positions[i] * scale;
                weights[i] = new BoneWeight
                {
                    boneIndex0 = remap[BoneIndices[i, 0]], weight0 = BoneWeights[i, 0],
                    boneIndex1 = remap[BoneIndices[i, 1]], weight1 = BoneWeights[i, 1],
                    boneIndex2 = remap[BoneIndices[i, 2]], weight2 = BoneWeights[i, 2],
                    boneIndex3 = remap[BoneIndices[i, 3]], weight3 = BoneWeights[i, 3],
                };
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.normals = Normals;
            mesh.uv = Uvs;
            mesh.boneWeights = weights;
            mesh.bindposes = bindposes;
            mesh.subMeshCount = SubMeshes.Length;
            for (int s = 0; s < SubMeshes.Length; s++)
                mesh.SetTriangles(SubMeshes[s], s);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Vector3 ReadVector3(BinaryReader reader) =>
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}
