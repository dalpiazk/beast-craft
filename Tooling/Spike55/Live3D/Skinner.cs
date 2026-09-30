using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>CPU linear-blend skinning: turns a GltfSkinnedModel's bind-pose vertices into a
    /// world-space MonoGame vertex array, given one skin matrix per joint (see
    /// AnimatedPose.ComputeSkinMatrices). Runs once per beast instance per frame -- its wall-clock cost
    /// is the spike's "skinning time" stat (see StatsTracker / Game1.Update).</summary>
    public static class Skinner
    {
        public static void SkinToBuffer(GltfSkinnedModel model, Matrix4x4[] jointSkinMatrices, VertexPositionNormalTexture[] destination)
        {
            var positions = model.Positions;
            var normals = model.Normals;
            var uvs = model.TexCoords;
            var jointIdx = model.JointIndices;
            var jointW = model.JointWeights;

            for (int i = 0; i < positions.Length; i++)
            {
                var ji = jointIdx[i];
                var jw = jointW[i];
                Vector3 pos = Vector3.Zero;
                Vector3 nrm = Vector3.Zero;

                AccumulateInfluence(jw.X, (int)ji.X, jointSkinMatrices, positions[i], normals[i], ref pos, ref nrm);
                AccumulateInfluence(jw.Y, (int)ji.Y, jointSkinMatrices, positions[i], normals[i], ref pos, ref nrm);
                AccumulateInfluence(jw.Z, (int)ji.Z, jointSkinMatrices, positions[i], normals[i], ref pos, ref nrm);
                AccumulateInfluence(jw.W, (int)ji.W, jointSkinMatrices, positions[i], normals[i], ref pos, ref nrm);

                if (nrm.LengthSquared() > 1e-12f)
                    nrm = Vector3.Normalize(nrm);

                destination[i].Position = new XnaVector3(pos.X, pos.Y, pos.Z);
                destination[i].Normal = new XnaVector3(nrm.X, nrm.Y, nrm.Z);
                destination[i].TextureCoordinate = new XnaVector2(uvs[i].X, uvs[i].Y);
            }
        }

        private static void AccumulateInfluence(float weight, int jointIndex, Matrix4x4[] skin, Vector3 localPos, Vector3 localNormal, ref Vector3 pos, ref Vector3 nrm)
        {
            if (weight <= 0f || jointIndex < 0 || jointIndex >= skin.Length)
                return;
            var m = skin[jointIndex];
            pos += weight * Vector3.Transform(localPos, m);
            nrm += weight * Vector3.TransformNormal(localNormal, m);
        }

        /// <summary>Rigid (unskinned) transform -- used for the cosmetic crest attachment, which has no
        /// skin of its own and is simply placed at the head bone's current world matrix each frame.</summary>
        public static void TransformToBuffer(GltfSkinnedModel model, Matrix4x4 transform, VertexPositionNormalTexture[] destination)
        {
            var positions = model.Positions;
            var normals = model.Normals;
            var uvs = model.TexCoords;
            for (int i = 0; i < positions.Length; i++)
            {
                var pos = Vector3.Transform(positions[i], transform);
                var nrm = Vector3.Normalize(Vector3.TransformNormal(normals[i], transform));
                destination[i].Position = new XnaVector3(pos.X, pos.Y, pos.Z);
                destination[i].Normal = new XnaVector3(nrm.X, nrm.Y, nrm.Z);
                destination[i].TextureCoordinate = new XnaVector2(uvs[i].X, uvs[i].Y);
            }
        }
    }
}
