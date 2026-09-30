using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaVector4 = Microsoft.Xna.Framework.Vector4;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>One vertex of the swarm's merged static VertexBuffer (see GpuMesh.BuildSwarmMerged and
    /// Toon.fx's "Fifth pass" section). GPU-skinned via a per-instance-sliced bone array
    /// (Toon.fx's SwarmBones[]), the same technique as the Griffin's Bones[] -- NOT Vertex Animation
    /// Textures: an earlier version of this pass sampled a texture in the vertex shader instead, which
    /// MonoGame 3.8.5's effect compiler cannot compile at all for the OpenGL profile (confirmed with a
    /// minimal repro -- see Toon.fx's header comment). BlendIndicesLocal is 0..2 (local to one
    /// swarmling's 3-bone rig); InstanceId selects which swarmling's slice of SwarmBones[] those local
    /// indices are offset into.</summary>
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
