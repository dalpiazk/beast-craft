using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaVector4 = Microsoft.Xna.Framework.Vector4;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Bind-pose vertex for GPU skinning: position/normal/uv plus up to 4 joint
    /// indices/weights (BLENDINDICES0/BLENDWEIGHT0 -- matches Toon.fx's VSInput). One static
    /// VertexBuffer of these is built per model (body, crest) in Game1.LoadContent and shared by every
    /// BeastInstance; only the `Bones` palette effect parameter changes per instance per frame -- see
    /// Toon.fx's header comment for why (GC/driver-pressure fix vs. the earlier CPU-skin-into-a-
    /// per-instance-DynamicVertexBuffer approach).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SkinnedVertex : IVertexType
    {
        public XnaVector3 Position;
        public XnaVector3 Normal;
        public XnaVector2 TexCoord;
        public XnaVector4 BlendIndices;
        public XnaVector4 BlendWeight;

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.BlendIndices, 0),
            new VertexElement(48, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
