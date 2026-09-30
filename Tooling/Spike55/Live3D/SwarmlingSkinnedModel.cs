using System;
using System.IO;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Loads the raw binary output of Tooling/Spike55/blender_export_vat_swarmling.py:
    /// swarmling_mesh.bin (bind-pose position/normal/uv/blend-indices/blend-weight per vertex, plus
    /// indices) and swarmling_bones.bin (every frame's per-bone skin matrix, pre-baked in Blender -- a
    /// handful of frames x 3 bones, tiny). GPU-skinned at runtime via Toon.fx's SwarmBones[] array (see
    /// SwarmVertex's doc comment for why this replaced an original Vertex-Animation-Texture design: VTF
    /// doesn't compile under MonoGame's OpenGL effect profile at all). No SharpGLTF/glTF involved for
    /// this asset -- same reasoning as the VAT design it replaced: avoids glTF-export vertex-reordering
    /// pitfalls for an asset whose vertex order must line up 1:1 with a side-channel data file (here,
    /// per-frame bone matrices instead of per-frame VAT positions). Bone matrices are pre-baked per
    /// frame in Blender (BoneFrames below), not evaluated at runtime via AnimatedPose -- simpler at
    /// runtime (no hierarchy walk needed per instance, just an index into BoneFrames[frame]) and just as
    /// cheap for this pass's short (20-frame) Idle+Move loop.</summary>
    public sealed class SwarmlingSkinnedModel
    {
        public int VertexCount;
        public int IndexCount;
        public int BoneCount;
        public int[] Indices;
        public System.Numerics.Vector3[] BindPosition;
        public System.Numerics.Vector3[] BindNormal;
        public System.Numerics.Vector2[] BindUv;
        public System.Numerics.Vector4[] BlendIndices;
        public System.Numerics.Vector4[] BlendWeight;

        public int FrameCount;
        public int IdleFrames;
        public int MoveFrames;
        public int Fps;
        public float TargetHeight;

        /// <summary>[frame][bone] -> object-local skin matrix (System.Numerics, row-major, matching
        /// every other matrix convention in this project -- see AnimatedPose.cs's doc comment).</summary>
        public System.Numerics.Matrix4x4[][] BoneFrames;

        public byte[] BaseColorImageBytes;

        public static SwarmlingSkinnedModel Load(string contentDir)
        {
            var model = new SwarmlingSkinnedModel();

            using (var fs = File.OpenRead(Path.Combine(contentDir, "swarmling_mesh.bin")))
            using (var br = new BinaryReader(fs))
            {
                int vertexCount = br.ReadInt32();
                int indexCount = br.ReadInt32();
                int boneCount = br.ReadInt32();
                model.VertexCount = vertexCount;
                model.IndexCount = indexCount;
                model.BoneCount = boneCount;

                model.BindPosition = new System.Numerics.Vector3[vertexCount];
                model.BindUv = new System.Numerics.Vector2[vertexCount];
                model.BindNormal = new System.Numerics.Vector3[vertexCount];
                model.BlendIndices = new System.Numerics.Vector4[vertexCount];
                model.BlendWeight = new System.Numerics.Vector4[vertexCount];

                for (int i = 0; i < vertexCount; i++)
                {
                    float px = br.ReadSingle(), py = br.ReadSingle(), pz = br.ReadSingle();
                    float u = br.ReadSingle(), v = br.ReadSingle();
                    float nx = br.ReadSingle(), ny = br.ReadSingle(), nz = br.ReadSingle();
                    br.ReadSingle(); // pad
                    float bi0 = br.ReadSingle(), bi1 = br.ReadSingle(), bi2 = br.ReadSingle(), bi3 = br.ReadSingle();
                    float bw0 = br.ReadSingle(), bw1 = br.ReadSingle(), bw2 = br.ReadSingle(), bw3 = br.ReadSingle();

                    model.BindPosition[i] = new System.Numerics.Vector3(px, py, pz);
                    model.BindUv[i] = new System.Numerics.Vector2(u, v);
                    model.BindNormal[i] = new System.Numerics.Vector3(nx, ny, nz);
                    model.BlendIndices[i] = new System.Numerics.Vector4(bi0, bi1, bi2, bi3);
                    model.BlendWeight[i] = new System.Numerics.Vector4(bw0, bw1, bw2, bw3);
                }

                model.Indices = new int[indexCount];
                for (int i = 0; i < indexCount; i++)
                    model.Indices[i] = br.ReadUInt16();
            }

            string metaJson = File.ReadAllText(Path.Combine(contentDir, "swarmling_meta.json"));
            model.FrameCount = ReadJsonInt(metaJson, "frameCount");
            model.IdleFrames = ReadJsonInt(metaJson, "idleFrames");
            model.MoveFrames = ReadJsonInt(metaJson, "moveFrames");
            model.Fps = ReadJsonInt(metaJson, "fps");
            model.TargetHeight = ReadJsonFloat(metaJson, "targetHeight");

            using (var fs = File.OpenRead(Path.Combine(contentDir, "swarmling_bones.bin")))
            using (var br = new BinaryReader(fs))
            {
                model.BoneFrames = new System.Numerics.Matrix4x4[model.FrameCount][];
                for (int f = 0; f < model.FrameCount; f++)
                {
                    model.BoneFrames[f] = new System.Numerics.Matrix4x4[model.BoneCount];
                    for (int b = 0; b < model.BoneCount; b++)
                    {
                        float m11 = br.ReadSingle(), m12 = br.ReadSingle(), m13 = br.ReadSingle(), m14 = br.ReadSingle();
                        float m21 = br.ReadSingle(), m22 = br.ReadSingle(), m23 = br.ReadSingle(), m24 = br.ReadSingle();
                        float m31 = br.ReadSingle(), m32 = br.ReadSingle(), m33 = br.ReadSingle(), m34 = br.ReadSingle();
                        float m41 = br.ReadSingle(), m42 = br.ReadSingle(), m43 = br.ReadSingle(), m44 = br.ReadSingle();
                        model.BoneFrames[f][b] = new System.Numerics.Matrix4x4(
                            m11, m12, m13, m14,
                            m21, m22, m23, m24,
                            m31, m32, m33, m34,
                            m41, m42, m43, m44);
                    }
                }
            }

            model.BaseColorImageBytes = File.ReadAllBytes(Path.Combine(contentDir, "swarmling_texture.png"));

            return model;
        }

        private static int ReadJsonInt(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
                throw new InvalidOperationException($"swarmling_meta.json missing key '{key}'");
            idx += needle.Length;
            while (idx < json.Length && json[idx] == ' ')
                idx++;
            int end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
                end++;
            return int.Parse(json.Substring(idx, end - idx));
        }

        private static float ReadJsonFloat(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int idx = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0)
                throw new InvalidOperationException($"swarmling_meta.json missing key '{key}'");
            idx += needle.Length;
            while (idx < json.Length && json[idx] == ' ')
                idx++;
            int end = idx;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '.'))
                end++;
            return float.Parse(json.Substring(idx, end - idx), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
