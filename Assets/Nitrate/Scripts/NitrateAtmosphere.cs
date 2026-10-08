using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Sends the scene state the atmosphere and film shaders need, as global shader properties, every frame:
    /// the moon direction, the figure's plane (fog is measured behind it), the beam box, the lantern and the film clock.
    /// The look itself (colours, densities) lives on the Nitrate/Atmosphere and Nitrate/Film materials.
    /// </summary>
    [ExecuteAlways]
    public class NitrateAtmosphere : MonoBehaviour
    {
        [Tooltip("The directional light that plays the moon. It must cast shadows (hard, one cascade).")]
        public Light moon;
        [ColorUsage(false, true)] public Color moonColor = new Color(0.85f, 0.9f, 1f) * 1.2f;

        [Tooltip("Fog starts at this object's plane and grows behind it. Put it on the figure.")]
        public Transform fogPlane;
        [Tooltip("Optional. The plane faces this camera's horizontal forward direction. Defaults to the main camera.")]
        public Camera viewCamera;

        [Tooltip("Beams only exist inside this box (world space). Keep it tight: every step outside it is wasted.")]
        public Vector3 beamBoxCenter = new Vector3(0f, 18f, 40f);
        public Vector3 beamBoxSize = new Vector3(120f, 40f, 150f);

        public Transform lantern;
        public float lanternIntensity = 3f;
        [ColorUsage(false, true)] public Color lanternColor = new Color(1f, 0.62f, 0.3f);

        [Header("Wind")]
        [Tooltip("Where the wind blows to (horizontal). It also sets which way the grass leans and the scarf trails.")]
        public Vector3 windDirection = new Vector3(-1f, 0f, 0.25f);
        [Tooltip("Bend of a fully flexible tip in a full gust, metres.")]
        public float windStrength = 0.35f;
        [Tooltip("How fast gusts roll across the scene, m/s. You can watch them travel through the grass.")]
        public float gustSpeed = 12f;
        [Range(0f, 1f)] public float flutter = 0.25f;
        [Tooltip("Flutter frequency. 16/15 Hz repeats exactly 16 times in the 15 s loop.")]
        public float flutterFrequency = 16f / 15f;

        [Tooltip("64x64 single-channel blue noise (point filter, no sRGB, no mipmaps).")]
        public Texture2D blueNoise;
        [Tooltip("Grain, flicker and the beam noise change this many times per second. 24 = film.")]
        public float filmFramesPerSecond = 24f;

        static readonly int s_LightDir = Shader.PropertyToID("_NitrateLightDir");
        static readonly int s_MoonColor = Shader.PropertyToID("_NitrateMoonColor");
        static readonly int s_FogPlane = Shader.PropertyToID("_NitrateFogPlane");
        static readonly int s_BoxMin = Shader.PropertyToID("_NitrateBoxMin");
        static readonly int s_BoxMax = Shader.PropertyToID("_NitrateBoxMax");
        static readonly int s_LanternPos = Shader.PropertyToID("_NitrateLanternPos");
        static readonly int s_LanternColor = Shader.PropertyToID("_NitrateLanternColor");
        static readonly int s_FilmFrame = Shader.PropertyToID("_NitrateFilmFrame");
        static readonly int s_BlueNoise = Shader.PropertyToID("_NitrateBlueNoise");
        static readonly int s_Wind = Shader.PropertyToID("_NitrateWind");
        static readonly int s_WindParams = Shader.PropertyToID("_NitrateWindParams");

        static Vector3 s_WindDir = Vector3.left;
        static float s_GustSpeed = 12f;

        /// <summary>The shader's gust function (NitrateCommon.hlsl), for scripts: about 0.2..1.0 at a world position.</summary>
        public static float Gust(Vector3 positionWS)
        {
            float t = NitrateClock.Time;
            float along = Vector3.Dot(positionWS, s_WindDir) / Mathf.Max(s_GustSpeed, 0.01f);
            return 0.55f + 0.3f * Mathf.Sin(2f * Mathf.PI * (t - along) / 7.5f) + 0.15f * Mathf.Sin(2f * Mathf.PI * (t - along) / 3.75f + 1.3f);
        }

        void OnEnable() => Apply();
        void LateUpdate() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            if (moon != null)
            {
                Vector3 toMoon = -moon.transform.forward;
                Shader.SetGlobalVector(s_LightDir, new Vector4(toMoon.x, toMoon.y, toMoon.z, 0f));
            }
            Shader.SetGlobalColor(s_MoonColor, moonColor);

            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            Vector3 normal = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.forward;
            Vector3 point = fogPlane != null ? fogPlane.position : Vector3.zero;
            Shader.SetGlobalVector(s_FogPlane, new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point)));

            Vector3 half = beamBoxSize * 0.5f;
            Shader.SetGlobalVector(s_BoxMin, beamBoxCenter - half);
            Shader.SetGlobalVector(s_BoxMax, beamBoxCenter + half);

            Vector3 lanternPos = lantern != null ? lantern.position : Vector3.zero;
            float intensity = lantern != null && lantern.gameObject.activeInHierarchy ? lanternIntensity : 0f;
            Shader.SetGlobalVector(s_LanternPos, new Vector4(lanternPos.x, lanternPos.y, lanternPos.z, intensity));
            Shader.SetGlobalColor(s_LanternColor, lanternColor);

            // The clock follows Time.time in Play mode, which is deterministic under Unity Recorder's fixed frame rate,
            // so recorded loops repeat exactly. In the editor it keeps running outside Play mode too.
            Shader.SetGlobalFloat(s_FilmFrame, Mathf.Floor(NitrateClock.Time * filmFramesPerSecond));
            if (blueNoise != null)
                Shader.SetGlobalTexture(s_BlueNoise, blueNoise);

            Vector3 dir = new Vector3(windDirection.x, 0f, windDirection.z);
            s_WindDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.left;
            s_GustSpeed = gustSpeed;
            Shader.SetGlobalVector(s_Wind, new Vector4(s_WindDir.x, 0f, s_WindDir.z, windStrength));
            Shader.SetGlobalVector(s_WindParams, new Vector4(NitrateClock.Time, gustSpeed, flutter, flutterFrequency));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.6f);
            Gizmos.DrawWireCube(beamBoxCenter, beamBoxSize);
        }
    }
}
