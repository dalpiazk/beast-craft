// Spike #55, fourth pass: custom toon + inverted-hull outline effect, per the task brief --
// "2-3 band ramp over the texture, cool #7C7AAE shadow tint, no pure black, warm key light, plus an
// inverted-hull ink-plum #2E2A45 outline pass". Compiled by the MonoGame content pipeline
// (Content.mgcb -> Content/bin/DesktopGL/Effects/Toon.xnb), loaded at runtime with Content.Load<Effect>.
//
// Vertices arrive already CPU-skinned and already in world space (Live3D's GltfSkinnedModel bakes
// the per-instance placement into the skin step, one instance at a time -- see BeastInstance.cs) --
// this effect only applies ViewProjection, so no per-instance World matrix or bone palette is needed
// here. Two techniques:
//   Toon    -- the banded-lighting beast/crest material.
//   Outline -- same vertex data, pushed out along the vertex normal by OutlineThickness and drawn
//              flat/unlit in OutlineColor with front-face culling reversed (set from C#, see
//              ToonEffectWrapper/Game1's RasterizerState swap) so only the silhouette fringe shows.

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 ViewProjection;

float3 LightDirection = normalize(float3(0.45, 0.65, 0.60));   // warm key, matches the Blender pass's sun angle
float3 LightColor = float3(1.0, 0.88, 0.68);
float3 ShadowTint = float3(0.4863, 0.4784, 0.6824);             // #7C7AAE, cool shadow -- never pure black
float3 HighlightBoost = float3(1.08, 1.0, 0.85);                // slight warm lift on the top band
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
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 NormalWS : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
};

// ---------------------------------------------------------------------------
// Toon technique: 3-band diffuse ramp multiplied onto the base-colour texture.
// Band thresholds/colours mirror blender_export_live.py's source material so the real-time look
// stays close to the pre-rendered toon passes (docs/spikes/055-3d-mini-spike.md sections 2.6/2.7).
// ---------------------------------------------------------------------------
VSOutput VS_Toon(VSInput input)
{
    VSOutput output;
    output.Position = mul(float4(input.Position, 1.0), ViewProjection);
    output.NormalWS = input.Normal;
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
// Outline technique: push the same skinned vertices out along their normal, flat ink-plum, unlit.
// Front-face culling is reversed from C# (RasterizerState.CullClockwise) before this pass, so only
// the expanded shell's back faces -- the silhouette fringe -- render.
// ---------------------------------------------------------------------------
VSOutput VS_Outline(VSInput input)
{
    VSOutput output;
    float3 expanded = input.Position + normalize(input.Normal) * OutlineThickness;
    output.Position = mul(float4(expanded, 1.0), ViewProjection);
    output.NormalWS = input.Normal;
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
