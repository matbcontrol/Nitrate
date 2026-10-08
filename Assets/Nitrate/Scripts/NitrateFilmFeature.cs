using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Nitrate
{
    /// <summary>
    /// The film print: monochrome tone curve, hard vignette, 24 Hz grain, flicker, gate weave, dust and scratches,
    /// and a final dither, in one full-screen pass after URP's post-processing.
    /// Needs HDR Precision = 64 Bits in the URP asset: the default R11G11B10 buffer stores blue with a 5-bit mantissa,
    /// which tints grey gradients and swallows the dither before the image reaches the 8-bit screen.
    /// </summary>
    public class NitrateFilmFeature : ScriptableRendererFeature
    {
        public Material material;
        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        FilmPass m_Pass;

        public override void Create()
        {
            m_Pass = new FilmPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null)
                return;

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            m_Pass.Setup(material);
            m_Pass.renderPassEvent = injectionPoint;
            m_Pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(m_Pass);
        }

        class FilmPass : ScriptableRenderPass
        {
            static readonly int s_BlitTexture = Shader.PropertyToID("_BlitTexture");
            static readonly int s_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

            Material m_Material;

            class PassData
            {
                public Material material;
                public TextureHandle source;
                public MaterialPropertyBlock properties;
            }

            public FilmPass()
            {
                profilingSampler = new ProfilingSampler("Nitrate Film");
            }

            public void Setup(Material material) => m_Material = material;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                    return;

                TextureHandle source = resources.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_NitrateFilmColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var data, profilingSampler))
                {
                    data.material = m_Material;
                    data.source = source;
                    data.properties ??= new MaterialPropertyBlock();
                    builder.UseTexture(source);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    {
                        d.properties.Clear();
                        d.properties.SetTexture(s_BlitTexture, d.source);
                        d.properties.SetVector(s_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.properties);
                    });
                }

                resources.cameraColor = destination;
            }
        }
    }
}
