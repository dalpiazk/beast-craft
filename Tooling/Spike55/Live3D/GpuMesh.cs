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

        /// <summary>Builds ONE merged static VertexBuffer/IndexBuffer containing `instanceCount` copies
        /// of a GltfSkinnedModel's bind-pose, skinned mesh -- the "merged-batch" swarm path
        /// (Tooling/Spike55's fifth-pass task brief: MonoGame's GLES/Android backend has no supported
        /// path for instanced geometry drawing, so this bakes N copies into one buffer instead). Every
        /// copy carries the SAME local BlendIndices (0..model.Joints.Length-1) plus which copy it is
        /// within THIS BATCH (InstanceId, 0..instanceCount-1 -- not a global swarm index: the fix-round
        /// register-budget redesign draws the swarm in fixed-size batches, each with its own merged
        /// buffer and its own slice of Toon.fx's SwarmBoneRows[] uploaded before that batch's draw calls
        /// -- see Game1.cs's _swarmBatches/UpdateSwarmBones and Toon.fx's "Fifth pass" section for why a
        /// single flat 24-swarmling bone array no longer fits GLES 3.0's guaranteed-minimum 256 vec4
        /// vertex uniform register budget at this rig's bone count). Cheap to build: `instanceCount`
        /// array copies, done once at scene setup, not per frame.</summary>
        public static GpuMesh BuildSwarmMerged(GraphicsDevice device, GltfSkinnedModel model, int instanceCount)
        {
            int baseVerts = model.Positions.Length;
            int baseIndices = model.Indices.Length;
            var verts = new SwarmVertex[baseVerts * instanceCount];
            for (int inst = 0; inst < instanceCount; inst++)
            {
                int offset = inst * baseVerts;
                for (int v = 0; v < baseVerts; v++)
                {
                    var p = model.Positions[v];
                    var nrm = model.Normals[v];
                    var uv = model.TexCoords[v];
                    var bi = model.JointIndices[v];
                    var bw = model.JointWeights[v];
                    verts[offset + v] = new SwarmVertex
                    {
                        Position = new XnaVector3(p.X, p.Y, p.Z),
                        Normal = new XnaVector3(nrm.X, nrm.Y, nrm.Z),
                        TexCoord = new XnaVector2(uv.X, uv.Y),
                        BlendIndicesLocal = new XnaVector4(bi.X, bi.Y, bi.Z, bi.W),
                        BlendWeight = new XnaVector4(bw.X, bw.Y, bw.Z, bw.W),
                        InstanceId = inst,
                    };
                }
            }
            var vb = new VertexBuffer(device, SwarmVertex.VertexDeclaration, verts.Length, BufferUsage.WriteOnly);
            vb.SetData(verts);

            var indices = new int[baseIndices * instanceCount];
            for (int inst = 0; inst < instanceCount; inst++)
            {
                int vOffset = inst * baseVerts;
                int iOffset = inst * baseIndices;
                for (int i = 0; i < baseIndices; i++)
                    indices[iOffset + i] = model.Indices[i] + vOffset;
            }
            var ib = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            var shorts = new short[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                shorts[i] = (short)indices[i];
            ib.SetData(shorts);

            return new GpuMesh { Vertices = vb, Indices = ib, TriangleCount = (baseIndices / 3) * instanceCount };
        }
    }
}
