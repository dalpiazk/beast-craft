using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaVector4 = Microsoft.Xna.Framework.Vector4;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>One vertex of one swarm BATCH's merged static VertexBuffer (see GpuMesh.BuildSwarmMerged
    /// and Toon.fx's "Fifth pass" section). GPU-skinned via a per-instance-sliced bone array
    /// (Toon.fx's SwarmBoneRows[]), the same underlying technique as the Griffin's Bones[] -- NOT Vertex
    /// Animation Textures: an earlier version of this pass sampled a texture in the vertex shader
    /// instead, which MonoGame 3.8.5's effect compiler cannot compile at all for the OpenGL profile
    /// (confirmed with a minimal repro -- see Toon.fx's header comment). BlendIndicesLocal is
    /// 0..SWARM_BONES_PER_INSTANCE-1 (local to one swarmling's 6-bone rig); InstanceId is 0..batch
    /// size-1, local to the batch this vertex's buffer belongs to (not a global 0..23 swarm index --
    /// see Game1.cs's _swarmBatches), selecting which swarmling's slice of that batch's SwarmBoneRows[]
    /// upload those local bone indices are offset into.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SwarmVertex : IVertexType
    {
        public XnaVector3 Position;
        public XnaVector3 Normal;
        public XnaVector2 TexCoord;
        public XnaVector4 BlendIndicesLocal;
        public XnaVector4 BlendWeight;
        public float InstanceId;

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.BlendIndices, 0),
            new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0),
            new VertexElement(64, VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 1));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
