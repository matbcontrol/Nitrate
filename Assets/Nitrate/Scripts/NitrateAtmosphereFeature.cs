using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Nitrate
{
    public enum BeamMode
    {
        [Tooltip("Ray march through the moon's shadow map: beams are cut by real geometry, from any camera angle.")]
        ShadowMarch,
        [Tooltip("Fallback: screen-space radial blur toward the moon (GPU Gems 3, ch. 13). Needs the moon on screen.")]
        RadialBlur,
        Off
    }

    public enum AtmosphereDebug
    {
        None,
        [Tooltip("White = lit by the moon, black = in its shadow, sampled at each pixel's surface. Must match the shadows you expect.")]
        ShadowAtDepth,
        BeamsOnly,
        FogAmount
    }

    /// <summary>
    /// The air of the scene. Every surface is black, so this pass makes the whole picture:
    /// sky, fog measured from the figure's plane, moonlight beams cut by the shadow map, and the lantern's glow.
    /// Beams are marched at low resolution, blurred without crossing depth edges, then upsampled the same way.
    /// </summary>
    public class NitrateAtmosphereFeature : ScriptableRendererFeature
    {
        public Material material;
        public BeamMode beamMode = BeamMode.ShadowMarch;
        [Tooltip("Beam texture is screen size divided by this. 4 = quarter resolution.")]
        [Range(1, 8)] public int beamDownsample = 4;
        [Tooltip("Shadow map samples per beam pixel.")]
        [Range(2, 48)] public int beamSteps = 16;
        [Tooltip("Blur the beam texture before upsampling. Without it the quarter-resolution noise shows as blocks.")]
        public bool beamBlur = true;
        [Tooltip("Depth blur ladder: sharp figure plane, soft near shapes, far layers softer with distance. Replaces Bokeh.")]
        public bool depthBlur = true;
        public AtmosphereDebug debugView = AtmosphereDebug.None;
        public RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        AtmospherePass m_Pass;

        public override void Create()
        {
            m_Pass = new AtmospherePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null)
                return;

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            m_Pass.Setup(this);
            m_Pass.renderPassEvent = injectionPoint;
            m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            m_Pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(m_Pass);
        }

        class AtmospherePass : ScriptableRenderPass
        {
            const int k_PassBeams = 0;
            const int k_PassBeamsRadial = 1;
            const int k_PassBlur = 2;
            const int k_PassComposite = 3;
            const int k_PassDownsample = 4;
            const int k_PassBlurColor = 5;
            const int k_PassHalf = 6;
            const int k_PassDepthBlur = 7;

            static readonly int s_BlitTexture = Shader.PropertyToID("_BlitTexture");
            static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
            static readonly int s_BeamTex = Shader.PropertyToID("_NitrateBeamTex");
            static readonly int s_BeamSize = Shader.PropertyToID("_NitrateBeamSize");
            static readonly int s_BeamSteps = Shader.PropertyToID("_NitrateBeamSteps");
            static readonly int s_BeamsOn = Shader.PropertyToID("_NitrateBeamsOn");
            static readonly int s_Debug = Shader.PropertyToID("_NitrateDebug");
            static readonly int s_BlurDir = Shader.PropertyToID("_NitrateBlurDir");
            static readonly int s_Blur1 = Shader.PropertyToID("_NitrateBlur1");
            static readonly int s_Blur2 = Shader.PropertyToID("_NitrateBlur2");
            static readonly int s_Blur1Size = Shader.PropertyToID("_NitrateBlur1Size");
            static readonly int s_Blur2Size = Shader.PropertyToID("_NitrateBlur2Size");
            static readonly int s_GlowLate = Shader.PropertyToID("_NitrateGlowLate");

            NitrateAtmosphereFeature m_Feature;
            readonly ProfilingSampler m_BeamsSampler = new ProfilingSampler("Nitrate Beams");
            readonly ProfilingSampler m_BlurSampler = new ProfilingSampler("Nitrate Beams Blur");
            readonly ProfilingSampler m_CompositeSampler = new ProfilingSampler("Nitrate Composite");
            readonly ProfilingSampler m_DepthBlurSampler = new ProfilingSampler("Nitrate Depth Blur");

            class PassData
            {
                public Material material;
                public int pass;
                public TextureHandle source;
                public TextureHandle beams;
                public Vector4 beamSize;
                public float beamSteps;
                public float beamsOn;
                public float debug;
                public Vector2 blurDir;
                public TextureHandle blur1;
                public TextureHandle blur2;
                public Vector4 blur1Size;
                public Vector4 blur2Size;
                public float glowLate;
                public MaterialPropertyBlock properties;
            }

            public AtmospherePass()
            {
                profilingSampler = new ProfilingSampler("Nitrate Atmosphere");
            }

            public void Setup(NitrateAtmosphereFeature feature) => m_Feature = feature;

            // Per-draw values go through a MaterialPropertyBlock: a command buffer copies it when the draw is recorded,
            // so the Scene view and the Game view can render in the same frame with different textures and sizes.
            static void Draw(PassData d, RasterGraphContext context)
            {
                MaterialPropertyBlock p = d.properties;
                p.Clear();
                p.SetVector(s_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
                if (d.source.IsValid()) p.SetTexture(s_BlitTexture, d.source);
                p.SetTexture(s_BeamTex, d.beams.IsValid() ? (Texture)d.beams : Texture2D.blackTexture);
                p.SetVector(s_BeamSize, d.beamSize);
                p.SetFloat(s_BeamSteps, d.beamSteps);
                p.SetFloat(s_BeamsOn, d.beamsOn);
                p.SetFloat(s_Debug, d.debug);
                p.SetVector(s_BlurDir, d.blurDir);
                p.SetTexture(s_Blur1, d.blur1.IsValid() ? (Texture)d.blur1 : Texture2D.blackTexture);
                p.SetTexture(s_Blur2, d.blur2.IsValid() ? (Texture)d.blur2 : Texture2D.blackTexture);
                p.SetVector(s_Blur1Size, d.blur1Size);
                p.SetVector(s_Blur2Size, d.blur2Size);
                p.SetFloat(s_GlowLate, d.glowLate);
                context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, p);
            }

            PassData Fill(PassData data, int pass, Vector4 beamSize, float beamsOn)
            {
                data.material = m_Feature.material;
                data.pass = pass;
                data.source = TextureHandle.nullHandle;
                data.beams = TextureHandle.nullHandle;
                data.beamSize = beamSize;
                data.beamSteps = m_Feature.beamSteps;
                data.beamsOn = beamsOn;
                data.debug = (float)m_Feature.debugView;
                data.blurDir = Vector2.zero;
                data.blur1 = TextureHandle.nullHandle;
                data.blur2 = TextureHandle.nullHandle;
                data.glowLate = 0f;
                data.properties ??= new MaterialPropertyBlock();
                return data;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid())
                    return;

                bool beamsOn = m_Feature.beamMode != BeamMode.Off;
                RenderTextureDescriptor cameraDesc = cameraData.cameraTargetDescriptor;
                int div = Mathf.Max(1, m_Feature.beamDownsample);
                int width = Mathf.Max(1, cameraDesc.width / div);
                int height = Mathf.Max(1, cameraDesc.height / div);
                var beamSize = new Vector4(width, height, 1f / width, 1f / height);

                TextureHandle beams = TextureHandle.nullHandle;
                if (beamsOn)
                {
                    // R = in-scattered moonlight, G = eye depth of the texel (the blur and the upsample compare depths).
                    var beamDesc = new TextureDesc(width, height)
                    {
                        name = "_NitrateBeams",
                        colorFormat = GraphicsFormat.R16G16_SFloat,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        clearBuffer = false
                    };
                    beams = renderGraph.CreateTexture(beamDesc);

                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Nitrate Beams", out var data, m_BeamsSampler))
                    {
                        int pass = m_Feature.beamMode == BeamMode.ShadowMarch ? k_PassBeams : k_PassBeamsRadial;
                        Fill(data, pass, beamSize, 1f);
                        builder.UseTexture(resources.cameraDepthTexture);
                        // The shader samples the main light shadow map (a global texture set by URP's shadow pass).
                        // Declaring it keeps it alive until this pass and orders this pass after the shadow pass.
                        if (resources.mainShadowsTexture.IsValid())
                            builder.UseTexture(resources.mainShadowsTexture);
                        builder.SetRenderAttachment(beams, 0, AccessFlags.Write);
                        builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => Draw(d, context));
                    }

                    if (m_Feature.beamBlur)
                    {
                        beamDesc.name = "_NitrateBeamsBlurH";
                        TextureHandle horizontal = renderGraph.CreateTexture(beamDesc);
                        AddBlur(renderGraph, beams, horizontal, new Vector2(1f, 0f), beamSize);
                        beamDesc.name = "_NitrateBeamsBlurV";
                        TextureHandle vertical = renderGraph.CreateTexture(beamDesc);
                        AddBlur(renderGraph, horizontal, vertical, new Vector2(0f, 1f), beamSize);
                        beams = vertical;
                    }
                }

                // Composite: read the camera colour, write the final image into a new texture, make it the camera colour.
                TextureHandle source = resources.activeColorTexture;
                TextureDesc colorDesc = renderGraph.GetTextureDesc(source);
                colorDesc.name = "_NitrateAtmosphereColor";
                colorDesc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(colorDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Nitrate Composite", out var data, m_CompositeSampler))
                {
                    Fill(data, k_PassComposite, beamSize, beamsOn ? 1f : 0f);
                    data.source = source;
                    data.glowLate = m_Feature.depthBlur ? 1f : 0f; // the depth blur pass adds the lantern glow after blurring
                    data.beams = beams;
                    builder.UseTexture(source);
                    builder.UseTexture(resources.cameraDepthTexture);
                    if (beams.IsValid())
                        builder.UseTexture(beams);
                    if (resources.mainShadowsTexture.IsValid())
                        builder.UseTexture(resources.mainShadowsTexture); // for the ShadowAtDepth debug view
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => Draw(d, context));
                }

                resources.cameraColor = destination;

                if (m_Feature.depthBlur)
                    AddDepthBlur(renderGraph, resources, destination, cameraDesc);
            }

            static Vector4 Size(int w, int h) => new Vector4(w, h, 1f / w, 1f / h);

            // Quarter-res copy with depth, blurred; eighth-res copy, blurred; then a full-res pick by depth.
            void AddDepthBlur(RenderGraph renderGraph, UniversalResourceData resources, TextureHandle sharp, RenderTextureDescriptor cameraDesc)
            {
                int w1 = Mathf.Max(1, (cameraDesc.width + 3) / 4), h1 = Mathf.Max(1, (cameraDesc.height + 3) / 4);
                int w2 = Mathf.Max(1, (w1 + 1) / 2), h2 = Mathf.Max(1, (h1 + 1) / 2);
                Vector4 size1 = Size(w1, h1), size2 = Size(w2, h2);
                TextureHandle Create(string name, int w, int h) => renderGraph.CreateTexture(new TextureDesc(w, h)
                {
                    name = name, colorFormat = GraphicsFormat.R16G16B16A16_SFloat, filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, clearBuffer = false
                });

                TextureHandle q0 = Create("_NitrateQuarter", w1, h1);
                AddSimple("Nitrate Depth Blur Downsample", k_PassDownsample, sharp, q0, size1, Vector2.zero, true);
                TextureHandle q1 = Create("_NitrateQuarterH", w1, h1);
                AddSimple("Nitrate Depth Blur H", k_PassBlurColor, q0, q1, size1, new Vector2(1f, 0f), false);
                TextureHandle q2 = Create("_NitrateQuarterV", w1, h1);
                AddSimple("Nitrate Depth Blur V", k_PassBlurColor, q1, q2, size1, new Vector2(0f, 1f), false);
                TextureHandle e0 = Create("_NitrateEighth", w2, h2);
                AddSimple("Nitrate Depth Blur Half", k_PassHalf, q2, e0, size1, Vector2.zero, false);
                TextureHandle e1 = Create("_NitrateEighthH", w2, h2);
                AddSimple("Nitrate Depth Blur H2", k_PassBlurColor, e0, e1, size2, new Vector2(1f, 0f), false);
                TextureHandle e2 = Create("_NitrateEighthV", w2, h2);
                AddSimple("Nitrate Depth Blur V2", k_PassBlurColor, e1, e2, size2, new Vector2(0f, 1f), false);

                TextureDesc colorDesc = renderGraph.GetTextureDesc(sharp);
                colorDesc.name = "_NitrateDepthBlurColor";
                colorDesc.clearBuffer = false;
                TextureHandle output = renderGraph.CreateTexture(colorDesc);
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Nitrate Depth Blur", out var data, m_DepthBlurSampler))
                {
                    Fill(data, k_PassDepthBlur, size1, 0f);
                    data.source = sharp;
                    data.blur1 = q2;
                    data.blur2 = e2;
                    data.blur1Size = size1;
                    data.blur2Size = size2;
                    builder.UseTexture(sharp);
                    builder.UseTexture(q2);
                    builder.UseTexture(e2);
                    builder.UseTexture(resources.cameraDepthTexture);
                    builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => Draw(d, context));
                }
                resources.cameraColor = output;

                void AddSimple(string name, int pass, TextureHandle input, TextureHandle target, Vector4 size, Vector2 dir, bool readsDepth)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data, m_DepthBlurSampler))
                    {
                        Fill(data, pass, size, 0f);
                        data.source = input;
                        data.blurDir = dir;
                        builder.UseTexture(input);
                        if (readsDepth)
                            builder.UseTexture(resources.cameraDepthTexture);
                        builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                        builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => Draw(d, context));
                    }
                }
            }

            void AddBlur(RenderGraph renderGraph, TextureHandle input, TextureHandle output, Vector2 direction, Vector4 beamSize)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Nitrate Beams Blur", out var data, m_BlurSampler))
                {
                    Fill(data, k_PassBlur, beamSize, 1f);
                    data.source = input;
                    data.blurDir = direction;
                    builder.UseTexture(input);
                    builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => Draw(d, context));
                }
            }
        }
    }
}
