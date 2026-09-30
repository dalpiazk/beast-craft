using Microsoft.Xna.Framework.Graphics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaVector4 = Microsoft.Xna.Framework.Vector4;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>A GltfSkinnedModel's GPU-resident, bind-pose geometry: one static VertexBuffer (built
    /// once, shared by every BeastInstance) plus its IndexBuffer. See SkinnedVertex's doc comment for
    /// why this is static rather than per-instance.</summary>
    public sealed class GpuMesh
    {
        public VertexBuffer Vertices;
        public IndexBuffer Indices;
        public int TriangleCount;

        public static GpuMesh Build(GraphicsDevice device, GltfSkinnedModel model)
        {
            int n = model.Positions.Length;
            var verts = new SkinnedVertex[n];
            bool rigid = model.Joints.Length == 0; // e.g. crest_alt.glb: no skin at all
            for (int i = 0; i < n; i++)
            {
                var p = model.Positions[i];
                var nrm = model.Normals[i];
                var uv = model.TexCoords[i];
                verts[i].Position = new XnaVector3(p.X, p.Y, p.Z);
                verts[i].Normal = new XnaVector3(nrm.X, nrm.Y, nrm.Z);
                verts[i].TexCoord = new XnaVector2(uv.X, uv.Y);
                if (rigid)
                {
                    // No real skin: bind 100% to palette slot 0, which the caller sets to a single
                    // rigid world transform each frame (Game1's crest-attach transform) -- see
                    // Toon.fx's header comment.
                    verts[i].BlendIndices = XnaVector4.Zero;
                    verts[i].BlendWeight = new XnaVector4(1f, 0f, 0f, 0f);
                }
                else
                {
                    var ji = model.JointIndices[i];
                    var jw = model.JointWeights[i];
                    verts[i].BlendIndices = new XnaVector4(ji.X, ji.Y, ji.Z, ji.W);
                    verts[i].BlendWeight = new XnaVector4(jw.X, jw.Y, jw.Z, jw.W);
                }
            }

            var vb = new VertexBuffer(device, SkinnedVertex.VertexDeclaration, n, BufferUsage.WriteOnly);
            vb.SetData(verts);

            var indices = model.Indices;
            var shorts = new short[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                shorts[i] = (short)indices[i];
            var ib = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            ib.SetData(shorts);

            return new GpuMesh { Vertices = vb, Indices = ib, TriangleCount = model.TriangleCount };
        }
    }
}
