// Spike #55, fourth pass: custom toon + inverted-hull outline effect, per the task brief --
// "2-3 band ramp over the texture, cool #7C7AAE shadow tint, no pure black, warm key light, plus an
// inverted-hull ink-plum #2E2A45 outline pass". Compiled by the MonoGame content pipeline
// (Content.mgcb -> Content/bin/DesktopGL/Effects/Toon.xnb), loaded at runtime with Content.Load<Effect>.
//
// GPU skinning (lead-review fix round): vertices are bind-pose, in a single STATIC VertexBuffer
// shared by every instance (built once in Game1.LoadContent), carrying BLENDINDICES0/BLENDWEIGHT0 per
// vertex same as the source glTF's JOINTS_0/WEIGHTS_0. Game1 computes each instance's per-joint skin
// matrix (inverseBind * jointWorld * instanceWorld -- see AnimatedPose.cs, unchanged) on the CPU every
// frame (cheap: 11 joints, no vertex work) and uploads it as the `Bones` array param before each draw
// -- no per-frame vertex buffer rewrite. An earlier version of this file did the skin blend on the CPU
// into a per-instance DynamicVertexBuffer rewritten every frame; that showed up as GC/driver-pressure
// frame-time spikes at 12+ instances (see docs/spikes/055-3d-mini-spike.md's fourth-pass section for
// the before/after numbers) and is why this version moved the blend into the vertex shader instead.
//
// MAX_BONES = 16 comfortably covers this rig's 11 joints (root/body/neck/head/2 wings/4 legs/tail)
// with headroom, and 16 float4x4 = 64 vec4 vertex-uniform registers -- well inside GLSL ES 2.0's
// spec-minimum guaranteed 128 vec4 vertex uniform vectors (MonoGame's own SkinnedEffect budgets 72
// bones = 288 vec4, which would NOT fit that same minimum guarantee; this rig needs nowhere near that,
// so 16 was chosen deliberately small rather than copying SkinnedEffect's budget). The cosmetic crest
// attachment (no real skin) reuses the same vertex format and shader: every one of its vertices is
// authored with BlendIndices=(0,0,0,0) and BlendWeight=(1,0,0,0) (see Game1.LoadContent), so setting
// Bones[0] to its rigid head-bone-relative transform skins it as a single "bone" with no separate code
// path.
//
// Two techniques:
//   Toon    -- the banded-lighting beast/crest material.
//   Outline -- same vertex data, pushed out along the vertex normal by OutlineThickness and drawn
//              flat/unlit in OutlineColor with front-face culling reversed (set from C#, see
//              Game1's RasterizerState swap) so only the silhouette fringe shows.

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#define MAX_BONES 16

float4x4 ViewProjection;
float4x4 Bones[MAX_BONES];

// Lead-review fix round: the first tuning pushed the beast noticeably more saturated/orange than both
// the Meshy source texture and the approved illustration (confirmed by sampling pixels from both --
// the source texture's own gold, e.g. (226,163,89), is already close to the illustration's (218,164,75);
// the *shader's* LightColor of (1.0, 0.88, 0.68) was the dominant cause, not a texture colour-space bug
// -- MonoGame's default Texture2D.FromStream load (SurfaceFormat.Color) applies no gamma/sRGB
// conversion, so this shader's straight multiply is gamma-space compositing throughout, consistent
// with the rest of the (SpriteBatch, also gamma-space) game -- checked, not the cause). LightColor and
// HighlightBoost are now much closer to neutral so the texture's own colour carries through; ShadowTint
// (the brief's required cool #7C7AAE, never-pure-black shadow) is unchanged.
float3 LightDirection = normalize(float3(0.45, 0.65, 0.60));   // warm key, matches the Blender pass's sun angle
float3 LightColor = float3(1.0, 0.97, 0.92);
float3 ShadowTint = float3(0.4863, 0.4784, 0.6824);             // #7C7AAE, cool shadow -- never pure black
float3 HighlightBoost = float3(1.03, 1.0, 0.96);                // a light lift, not a warm push
float3 TintMultiply = float3(1.0, 1.0, 1.0);                    // colour-form cycling (key-driven, Game1)

float OutlineThickness = 0.012;
float3 OutlineColor = float3(0.1804, 0.1647, 0.2706);           // #2E2A45, ink-plum

texture BaseTexture;
sampler BaseSampler = sampler_state
{
    Texture = <BaseTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VSInput
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD0;
    float4 BlendIndices : BLENDINDICES0;
    float4 BlendWeight : BLENDWEIGHT0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 NormalWS : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
};

// Linear-blend GPU skin: bind-pose position/normal -> world space, via up to 4 weighted bone matrices.
// Shared by both the Toon and Outline vertex shaders below (Outline additionally pushes out along the
// skinned world-space normal by OutlineThickness).
void SkinPositionNormal(VSInput input, out float3 worldPos, out float3 worldNormal)
{
    float4x4 skin =
        Bones[(int)input.BlendIndices.x] * input.BlendWeight.x +
        Bones[(int)input.BlendIndices.y] * input.BlendWeight.y +
        Bones[(int)input.BlendIndices.z] * input.BlendWeight.z +
        Bones[(int)input.BlendIndices.w] * input.BlendWeight.w;

    worldPos = mul(float4(input.Position, 1.0), skin).xyz;
    worldNormal = mul(input.Normal, (float3x3)skin);
}

// ---------------------------------------------------------------------------
// Toon technique: 3-band diffuse ramp multiplied onto the base-colour texture.
// Band thresholds/colours mirror blender_export_live.py's source material so the real-time look
// stays close to the pre-rendered toon passes (docs/spikes/055-3d-mini-spike.md sections 2.6/2.7).
// ---------------------------------------------------------------------------
VSOutput VS_Toon(VSInput input)
{
    VSOutput output;
    float3 worldPos, worldNormal;
    SkinPositionNormal(input, worldPos, worldNormal);
    output.Position = mul(float4(worldPos, 1.0), ViewProjection);
    output.NormalWS = worldNormal;
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PS_Toon(VSOutput input) : SV_TARGET
{
    float3 n = normalize(input.NormalWS);
    float ndotl = dot(n, LightDirection) * 0.5 + 0.5; // half-lambert: avoids a hard black terminator

    float3 band;
    if (ndotl < 0.32)
        band = ShadowTint;
    else if (ndotl < 0.78)
        band = float3(1.0, 1.0, 1.0);
    else
        band = HighlightBoost;

    float4 baseColor = tex2D(BaseSampler, input.TexCoord);
    float3 lit = baseColor.rgb * band * TintMultiply * LightColor;
    return float4(lit, baseColor.a);
}

technique Toon
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_Toon();
        PixelShader = compile PS_SHADERMODEL PS_Toon();
    }
}

// ---------------------------------------------------------------------------
// Outline technique: push the same skinned vertices out along their (skinned, world-space) normal,
// flat ink-plum, unlit. Front-face culling is reversed from C# (RasterizerState.CullCounterClockwise)
// before this pass, so only the expanded shell's back faces -- the silhouette fringe -- render.
// ---------------------------------------------------------------------------
VSOutput VS_Outline(VSInput input)
{
    VSOutput output;
    float3 worldPos, worldNormal;
    SkinPositionNormal(input, worldPos, worldNormal);
    float3 expanded = worldPos + normalize(worldNormal) * OutlineThickness;
    output.Position = mul(float4(expanded, 1.0), ViewProjection);
    output.NormalWS = worldNormal;
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PS_Outline(VSOutput input) : SV_TARGET
{
    return float4(OutlineColor, 1.0);
}

technique Outline
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_Outline();
        PixelShader = compile PS_SHADERMODEL PS_Outline();
    }
}

// ===========================================================================
// Fifth pass: swarm rendering. ORIGINALLY built as Vertex Animation Textures
// (VAT: bake animated position/normal per frame into a texture, sample it in
// the vertex shader) -- **confirmed not buildable**: isolated to a 6-line
// minimal repro (a bare `tex2Dlod` in a vertex shader, no dynamic array
// indexing, no other complexity) and MonoGame 3.8.5's effect compiler fails
// it identically -- "invalid DCL register type for this shader model" /
// "TEXLD using undeclared sampler" inside MonoGame.Effect.ShaderData.
// CreateGLSL (the MojoShader-based DX9-bytecode-to-GLSL translation step the
// OpenGL profile goes through). This is a **hard MonoGame/MGFX toolchain
// limitation for the OpenGL profile, not a runtime GPU-support question** --
// stronger and more definitive than the mobile-budgets research's own
// "unverified on some Android GPUs" caveat (section 2.9): it doesn't build
// for DesktopGL at all, so it was never going to reach an Android GLES
// device to test in the first place. Real, confirmed, documented in
// docs/spikes/055-3d-mini-spike.md's fifth-pass section.
//
// Replaced with GPU skinning via a per-instance bone-ARRAY OFFSET instead --
// the same Bones[]-uniform-array + BLENDINDICES/BLENDWEIGHT pattern already
// proven working for the Griffin above (SkinPositionNormal), just with a
// bigger array (MAX_SWARM_INSTANCES swarmlings x SWARM_BONES_PER_INSTANCE
// bones each) and a per-vertex InstanceId selecting which instance's slice
// of that array to read. No texture sampling in the vertex shader at all --
// only dynamic uniform-array indexing, which Toon.fx's existing Bones[]
// technique already confirms this toolchain *does* support. The whole
// swarm still draws from one merged static vertex buffer in one draw call
// per pass (GpuMesh.BuildSwarmMerged); the per-frame CPU cost is now
// SWARM_BONES_PER_INSTANCE x instance-count small matrix computations
// (cheap, same order of cost as the Griffins' own per-instance bone work),
// not the "one float" VAT's CPU cost would have been -- a real, measured
// cost difference from the VAT design, not zero, but still small; see the
// gate report for the measured number.
// ===========================================================================
#define MAX_SWARM_INSTANCES 24
#define SWARM_BONES_PER_INSTANCE 3
#define SWARM_BONES_TOTAL (MAX_SWARM_INSTANCES * SWARM_BONES_PER_INSTANCE)

float4x4 SwarmBones[SWARM_BONES_TOTAL];

struct VSInputSwarm
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD0;
    float4 BlendIndicesLocal : BLENDINDICES0; // 0..SWARM_BONES_PER_INSTANCE-1, local to one swarmling
    float4 BlendWeight : BLENDWEIGHT0;
    float InstanceId : TEXCOORD1;             // which swarmling copy this vertex belongs to
};

void SkinSwarmPositionNormal(VSInputSwarm input, out float3 worldPos, out float3 worldNormal)
{
    float base = input.InstanceId * SWARM_BONES_PER_INSTANCE;
    float4x4 skin =
        SwarmBones[(int)(base + input.BlendIndicesLocal.x)] * input.BlendWeight.x +
        SwarmBones[(int)(base + input.BlendIndicesLocal.y)] * input.BlendWeight.y +
        SwarmBones[(int)(base + input.BlendIndicesLocal.z)] * input.BlendWeight.z +
        SwarmBones[(int)(base + input.BlendIndicesLocal.w)] * input.BlendWeight.w;

    worldPos = mul(float4(input.Position, 1.0), skin).xyz;
    worldNormal = mul(input.Normal, (float3x3)skin);
}

VSOutput VS_ToonSwarm(VSInputSwarm input)
{
    VSOutput output;
    float3 worldPos, worldNormal;
    SkinSwarmPositionNormal(input, worldPos, worldNormal);
    output.Position = mul(float4(worldPos, 1.0), ViewProjection);
    output.NormalWS = worldNormal;
    output.TexCoord = input.TexCoord;
    return output;
}

technique ToonSwarm
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_ToonSwarm();
        PixelShader = compile PS_SHADERMODEL PS_Toon();
    }
}

VSOutput VS_OutlineSwarm(VSInputSwarm input)
{
    VSOutput output;
    float3 worldPos, worldNormal;
    SkinSwarmPositionNormal(input, worldPos, worldNormal);
    float3 expanded = worldPos + normalize(worldNormal) * OutlineThickness;
    output.Position = mul(float4(expanded, 1.0), ViewProjection);
    output.NormalWS = worldNormal;
    output.TexCoord = input.TexCoord;
    return output;
}

technique OutlineSwarm
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_OutlineSwarm();
        PixelShader = compile PS_SHADERMODEL PS_Outline();
    }
}
