using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nitrate
{
    /// <summary>
    /// Layer toggles, High/Low presets and the frame-budget HUD. Each layer can be switched off so its cost
    /// reads as a difference in GPU milliseconds, and so the breakdown clip can strip the look layer by layer.
    /// GPU time needs Project Settings > Player > Other Settings > Frame Timing Stats.
    /// Settings that live in assets (URP asset, materials, volume profile) are restored when Play mode ends.
    /// </summary>
    public class NitrateControls : MonoBehaviour
    {
        [System.Serializable]
        public class Preset
        {
            public string name = "High";
            [Range(0.5f, 1f)] public float renderScale = 1f;
            [Range(2, 48)] public int beamSteps = 16;
            [Range(1, 8)] public int beamDownsample = 4;
            [Tooltip("Depth blur ladder (sharp figure plane, softer with distance).")]
            public bool depthBlur = true;
            [Range(2, 8)] public int bloomIterations = 4;
            public bool smaa = true;
            public int shadowResolution = 4096;
        }

        public UniversalRendererData rendererData;
        public Volume postVolume;
        public Material filmMaterial;
        [Tooltip("Material on every silhouette. The Lit view swaps it for litMaterial to show the bare geometry.")]
        public Material silhouetteMaterial;
        public Material litMaterial;
        public Camera mainCamera;

        public Preset high = new Preset();
        public Preset low = new Preset { name = "Low", renderScale = 0.75f, beamSteps = 10, bloomIterations = 3, smaa = false, shadowResolution = 2048 };
        public bool startLow;
        public float budgetMs = 16.67f;
        [Tooltip("Budget HUD. Off by default so it never covers the shot; H toggles it.")]
        public bool hudVisible;

        static readonly int s_Ember = Shader.PropertyToID("_Ember");

        NitrateAtmosphereFeature m_Atmosphere;
        NitrateFilmFeature m_Film;
        Bloom m_Bloom;
        UniversalAdditionalCameraData m_CameraData;
        Preset m_Current;
        bool m_LitView;
        readonly List<MeshRenderer> m_Swapped = new List<MeshRenderer>();

        readonly FrameTiming[] m_Timing = new FrameTiming[1];
        readonly StringBuilder m_Text = new StringBuilder(1024);
        float m_GpuMs, m_CpuMs;
        GUIStyle m_Style;

        // Asset state to restore.
        bool m_AtmosphereWasActive, m_FilmWasActive;
        BeamMode m_BeamModeWas;
        int m_StepsWas, m_DownsampleWas;
        AtmosphereDebug m_DebugWas;
        bool m_DepthBlurWas;
        float m_RenderScaleWas, m_EmberWas;
        int m_ShadowResWas;
        HDRColorBufferPrecision m_HdrWas;
        Color m_AmbientWas;

        public Preset Current => m_Current;
        public NitrateAtmosphereFeature Atmosphere => m_Atmosphere;
        public NitrateFilmFeature Film => m_Film;

        void OnEnable()
        {
            if (rendererData != null)
            {
                foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
                {
                    if (feature is NitrateAtmosphereFeature a) m_Atmosphere = a;
                    else if (feature is NitrateFilmFeature f) m_Film = f;
                }
            }
            if (postVolume != null)
            {
                postVolume.profile.TryGet(out m_Bloom);
            }
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera != null)
                m_CameraData = mainCamera.GetUniversalAdditionalCameraData();

            UniversalRenderPipelineAsset urp = UniversalRenderPipeline.asset;
            if (m_Atmosphere != null)
            {
                m_AtmosphereWasActive = m_Atmosphere.isActive;
                m_BeamModeWas = m_Atmosphere.beamMode;
                m_StepsWas = m_Atmosphere.beamSteps;
                m_DownsampleWas = m_Atmosphere.beamDownsample;
                m_DebugWas = m_Atmosphere.debugView;
                m_DepthBlurWas = m_Atmosphere.depthBlur;
            }
            if (m_Film != null) m_FilmWasActive = m_Film.isActive;
            if (filmMaterial != null) m_EmberWas = filmMaterial.GetFloat(s_Ember);
            if (urp != null)
            {
                m_RenderScaleWas = urp.renderScale;
                m_ShadowResWas = urp.mainLightShadowmapResolution;
                m_HdrWas = urp.hdrColorBufferPrecision;
            }
            m_AmbientWas = RenderSettings.ambientLight;

            ApplyPreset(startLow ? low : high);
        }

        void OnDisable()
        {
            SetLitView(false);
            if (m_Atmosphere != null)
            {
                m_Atmosphere.SetActive(m_AtmosphereWasActive);
                m_Atmosphere.beamMode = m_BeamModeWas;
                m_Atmosphere.beamSteps = m_StepsWas;
                m_Atmosphere.beamDownsample = m_DownsampleWas;
                m_Atmosphere.debugView = m_DebugWas;
                m_Atmosphere.depthBlur = m_DepthBlurWas;
            }
            if (m_Film != null) m_Film.SetActive(m_FilmWasActive);
            if (filmMaterial != null) filmMaterial.SetFloat(s_Ember, m_EmberWas);
            UniversalRenderPipelineAsset urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.renderScale = m_RenderScaleWas;
                urp.mainLightShadowmapResolution = m_ShadowResWas;
                urp.hdrColorBufferPrecision = m_HdrWas;
            }
            RenderSettings.ambientLight = m_AmbientWas;
        }

        public void ApplyPreset(Preset preset)
        {
            m_Current = preset;
            UniversalRenderPipelineAsset urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.renderScale = preset.renderScale;
                urp.mainLightShadowmapResolution = preset.shadowResolution;
            }
            if (m_Atmosphere != null)
            {
                m_Atmosphere.beamSteps = preset.beamSteps;
                m_Atmosphere.beamDownsample = preset.beamDownsample;
            }
            if (m_Atmosphere != null)
                m_Atmosphere.depthBlur = preset.depthBlur;
            if (m_Bloom != null)
                m_Bloom.maxIterations.value = preset.bloomIterations;
            if (m_CameraData != null)
            {
                m_CameraData.antialiasing = preset.smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                m_CameraData.antialiasingQuality = AntialiasingQuality.Low;
            }
        }

        // Layer switches, also used by the benchmark.
        public void SetSmaa(bool on)
        {
            if (m_CameraData != null)
                m_CameraData.antialiasing = on ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
        }
        public void SetFilm(bool on) { if (m_Film != null) m_Film.SetActive(on); }
        public void SetAtmosphere(bool on) { if (m_Atmosphere != null) m_Atmosphere.SetActive(on); }
        public void SetBeams(BeamMode mode) { if (m_Atmosphere != null) m_Atmosphere.beamMode = mode; }
        public void SetBloom(bool on) { if (m_Bloom != null) m_Bloom.active = on; }
        public void SetDepthOfField(bool on) { if (m_Atmosphere != null) m_Atmosphere.depthBlur = on; }
        public void SetHdr64(bool on)
        {
            if (UniversalRenderPipeline.asset != null)
                UniversalRenderPipeline.asset.hdrColorBufferPrecision = on ? HDRColorBufferPrecision._64Bits : HDRColorBufferPrecision._32Bits;
        }

        /// <summary>Shows the bare geometry: grey lit material and some ambient light instead of black silhouettes.</summary>
        public void SetLitView(bool on)
        {
            if (on == m_LitView || silhouetteMaterial == null || litMaterial == null)
                return;
            m_LitView = on;
            if (on)
            {
                m_Swapped.Clear();
                foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
                {
                    if (r.sharedMaterial != silhouetteMaterial)
                        continue;
                    r.sharedMaterial = litMaterial;
                    m_Swapped.Add(r);
                }
                RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.38f);
            }
            else
            {
                foreach (MeshRenderer r in m_Swapped)
                    if (r != null) r.sharedMaterial = silhouetteMaterial;
                m_Swapped.Clear();
                RenderSettings.ambientLight = m_AmbientWas;
            }
        }

        void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, m_Timing) > 0)
            {
                m_GpuMs = Mathf.Lerp(m_GpuMs, (float)m_Timing[0].gpuFrameTime, 0.1f);
                m_CpuMs = Mathf.Lerp(m_CpuMs, (float)m_Timing[0].cpuFrameTime, 0.1f);
            }

            Keyboard k = Keyboard.current;
            if (k == null)
                return;
            if (k.hKey.wasPressedThisFrame) hudVisible = !hudVisible;
            if (k.digit1Key.wasPressedThisFrame && m_Film != null) SetFilm(!m_Film.isActive);
            if (k.digit2Key.wasPressedThisFrame && m_Bloom != null) SetBloom(!m_Bloom.active);
            if (k.digit3Key.wasPressedThisFrame && m_Atmosphere != null) SetDepthOfField(!m_Atmosphere.depthBlur);
            if (k.digit4Key.wasPressedThisFrame && m_Atmosphere != null) SetBeams((BeamMode)(((int)m_Atmosphere.beamMode + 1) % 3));
            if (k.digit5Key.wasPressedThisFrame && m_Atmosphere != null) SetAtmosphere(!m_Atmosphere.isActive);
            if (k.digit6Key.wasPressedThisFrame) SetLitView(!m_LitView);
            if (k.lKey.wasPressedThisFrame) ApplyPreset(m_Current == high ? low : high);
            if (k.vKey.wasPressedThisFrame && m_Atmosphere != null) m_Atmosphere.debugView = (AtmosphereDebug)(((int)m_Atmosphere.debugView + 1) % 4);
            if (k.mKey.wasPressedThisFrame && filmMaterial != null) filmMaterial.SetFloat(s_Ember, filmMaterial.GetFloat(s_Ember) > 0.5f ? 0f : 1f);
        }

        void OnGUI()
        {
            if (!hudVisible)
                return;
            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
                m_Style.normal.textColor = new Color(0.92f, 0.92f, 0.92f); // the default can come out black in the Game view
            }

            string On(bool value) => value ? "<color=#9f9>ON</color>" : "<color=#f99>OFF</color>";
            m_Text.Clear();
            if (m_GpuMs > 0f)
                m_Text.AppendFormat("<b>GPU {0:0.00} ms</b>   CPU {1:0.00} ms   preset <b>{2}</b>\n", m_GpuMs, m_CpuMs, m_Current != null ? m_Current.name : "-");
            else
                m_Text.AppendFormat("<b>GPU n/a</b> (enable Frame Timing Stats)   preset <b>{0}</b>\n", m_Current != null ? m_Current.name : "-");
            m_Text.AppendFormat("[1] Film {0}   [2] Bloom {1}   [3] Depth of field {2}\n",
                On(m_Film != null && m_Film.isActive), On(m_Bloom != null && m_Bloom.active), On(m_Atmosphere != null && m_Atmosphere.depthBlur));
            if (m_Atmosphere != null)
                m_Text.AppendFormat("[4] Beams <b>{0}</b> ({1} steps, 1/{2} res)   [5] Atmosphere {3}\n",
                    m_Atmosphere.beamMode, m_Atmosphere.beamSteps, m_Atmosphere.beamDownsample, On(m_Atmosphere.isActive));
            m_Text.AppendFormat("[6] Lit view {0}   [V] debug: {1}   [L] preset   [M] ember\n", On(m_LitView), m_Atmosphere != null ? m_Atmosphere.debugView.ToString() : "-");
            m_Text.Append("Wheel: zoom   Space: loop   RMB drag: moon   C: cursor   H: hide");

            const float width = 520f, height = 116f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(10f, 10f, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(18f, 14f, width - 16f, height - 16f), m_Text.ToString(), m_Style);

            float fraction = m_GpuMs / budgetMs;
            var bar = new Rect(18f, height + 2f, width - 16f, 6f);
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = fraction < 0.75f ? new Color(0.4f, 0.9f, 0.4f) : fraction < 1f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.4f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(fraction), bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
