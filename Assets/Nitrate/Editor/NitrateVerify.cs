using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nitrate.EditorTools
{
    /// <summary>
    /// Batch-mode check: builds the scene, compiles the shaders, renders the hero frames and the debug views to PNG.
    /// Run: Unity.exe -batchmode -projectPath ... -executeMethod Nitrate.EditorTools.NitrateVerify.Run -shots &lt;folder&gt;
    /// </summary>
    public static class NitrateVerify
    {
        public static void Run()
        {
            int exitCode = 0;
            try
            {
                string folder = Arg("-shots") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "shots");
                Directory.CreateDirectory(folder);
                int width = int.Parse(Arg("-width") ?? "1920"), height = int.Parse(Arg("-height") ?? "1080");

                foreach (var name in new[] { "Nitrate/Atmosphere", "Nitrate/Graphs/NitrateFilm", "Nitrate/Graphs/NitrateSilhouette" })
                {
                    Shader shader = Shader.Find(name);
                    if (shader == null) { Debug.LogError("VERIFY shader not found " + name); exitCode = 2; continue; }
                    foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
                        Debug.Log($"VERIFY shader msg [{name}] {m.severity}: {m.message} line {m.line} {m.file}");
                    Debug.Log($"VERIFY shader {name} hasError={ShaderUtil.ShaderHasError(shader)}");
                }

                NitrateSceneBuilder.BuildScene();
                foreach (NitrateGenerator g in UnityEngine.Object.FindObjectsByType<NitrateGenerator>(FindObjectsInactive.Include))
                    Debug.Log($"VERIFY mesh {g.name}: {g.TriangleCount} triangles");

                var cam = Camera.main;
                var dolly = cam.GetComponent<NitrateDollyZoom>();
                var atmosphere = UnityEngine.Object.FindAnyObjectByType<NitrateAtmosphere>();
                var rotor = UnityEngine.Object.FindAnyObjectByType<NitrateRotor>();
                UniversalRendererData data = NitrateSceneBuilder.RendererData;
                NitrateAtmosphereFeature feature = null;
                NitrateFilmFeature film = null;
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                {
                    if (f is NitrateAtmosphereFeature a) feature = a;
                    if (f is NitrateFilmFeature b) film = b;
                }
                DepthOfField dof = null;

                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);

                void Shot(string file, float t, float sailAngle)
                {
                    dolly.t = t;
                    dolly.Apply();
                    if (dof != null) dof.focusDistance.value = dolly.Distance;
                    rotor.transform.localRotation = Quaternion.AngleAxis(sailAngle, Vector3.forward);
                    atmosphere.Apply();
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (!RenderPipeline.SupportsRenderRequest(cam, request)) { Debug.LogError("VERIFY render request unsupported"); return; }
                    RenderPipeline.SubmitRenderRequest(cam, request);
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(folder, file + ".png"), tex.EncodeToPNG());
                    Debug.Log("VERIFY metrics " + file + ": " + Metrics(tex));
                    Debug.Log($"VERIFY shot {file}: t={t} fov={dolly.Fov:0.0} distance={dolly.Distance:0.00} camera={cam.transform.position}");
                }

                // Probe: where Unity projects the hub and the moon, and where the shader's glow lands without the wide lobe.
                dolly.t = 0f; dolly.Apply();
                Vector3 toMoon = -atmosphere.moon.transform.forward;
                Debug.Log($"VERIFY probe hub={cam.WorldToViewportPoint(new Vector3(0f, 15f, 0f))} moon={cam.WorldToViewportPoint(cam.transform.position + toMoon * 1000f)} lensShift={cam.lensShift} physical={cam.usePhysicalProperties} proj={cam.projectionMatrix}");
                Material atmo = feature.material;
                Vector4 wideWas = atmo.GetVector("_GlowWide"), tightWas = atmo.GetVector("_GlowTight");
                atmo.SetVector("_GlowWide", Vector4.zero); atmo.SetVector("_GlowTight", Vector4.zero);
                feature.beamMode = BeamMode.Off;
                Shot("00_probe_moon_only", 0f, 45f);
                feature.beamMode = BeamMode.ShadowMarch;
                atmo.SetVector("_GlowWide", wideWas); atmo.SetVector("_GlowTight", tightWas);

                Shot("01_tele", 0f, 45f);
                Shot("02_tele_plus", 0f, 0f);
                Shot("03_t030", 0.3f, 45f);
                Shot("04_t060", 0.6f, 45f);
                Shot("05_wide", 1f, 45f);

                feature.debugView = AtmosphereDebug.ShadowAtDepth;
                Shot("10_debug_shadow_tele", 0f, 45f);
                feature.debugView = AtmosphereDebug.BeamsOnly;
                Shot("12_debug_beams_tele", 0f, 45f);
                Shot("13_debug_beams_wide", 1f, 45f);
                feature.debugView = AtmosphereDebug.FogAmount;
                Shot("14_debug_fog_wide", 1f, 45f);
                feature.debugView = AtmosphereDebug.None;
            }
            catch (Exception e)
            {
                Debug.LogError("VERIFY exception: " + e);
                exitCode = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode); // never close an interactive editor
        }

        /// <summary>Builds the Windows player. -out path\Nitrate.exe</summary>
        public static void Build()
        {
            int exitCode = 0;
            try
            {
                NitrateSceneBuilder.BuildScene();
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Nitrate/Scenes/Mill.unity" },
                    locationPathName = Arg("-out") ?? "Builds/Nitrate/Nitrate.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };
                UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
                Debug.Log($"VERIFY build {report.summary.result} size={report.summary.totalSize / (1024 * 1024)} MB errors={report.summary.totalErrors}");
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) exitCode = 3;
            }
            catch (Exception e)
            {
                Debug.LogError("VERIFY exception: " + e);
                exitCode = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode); // never close an interactive editor
        }

        /// <summary>
        /// Renders the open Mill scene's main camera into a PNG at a given dolly position and sail angle, without
        /// touching the editor otherwise. Returns the tonal fingerprint. Safe to call from an interactive editor.
        /// </summary>
        /// <summary>
        /// Renders one frame to a PNG and returns its tonal metrics. t is the dolly position. With time >= 0 the whole
        /// scene is posed at that moment of the clock (sails, wind, lantern, scarf, figure, film frame) and sailAngle
        /// is ignored; otherwise only the sails are set, to sailAngle.
        /// </summary>
        public static string RenderShot(string path, float t, float sailAngle, int width = 1920, int height = 1080, float filmFrame = -1f, float time = -1f)
        {
            var cam = Camera.main;
            var dolly = cam.GetComponent<NitrateDollyZoom>();
            var atmosphere = UnityEngine.Object.FindAnyObjectByType<NitrateAtmosphere>();
            var rotor = UnityEngine.Object.FindAnyObjectByType<NitrateRotor>();
            float tWas = dolly.t;
            Quaternion rotorWas = rotor.transform.localRotation;
            bool animateWas = NitrateClock.AnimateInEditMode;
            NitrateClock.AnimateInEditMode = false; // the edit-mode ticker must not move anything mid-render
            dolly.t = t;
            dolly.Apply();
            if (time >= 0f)
            {
                NitrateClock.Pinned = time;
                PoseAll();
            }
            else
                rotor.transform.localRotation = Quaternion.AngleAxis(sailAngle, Vector3.forward);
            atmosphere.Apply();
            if (filmFrame >= 0f)
                Shader.SetGlobalFloat("_NitrateFilmFrame", filmFrame); // step grain, flicker and beam noise like the player

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(cam, request);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            string metrics = Metrics(tex);
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            dolly.t = tWas;
            dolly.Apply();
            rotor.transform.localRotation = rotorWas;
            NitrateClock.Pinned = -1f;
            NitrateClock.AnimateInEditMode = animateWas;
            if (time >= 0f)
                PoseAll();
            return metrics;
        }

        static void PoseAll()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<NitrateRotor>(FindObjectsInactive.Exclude)) r.Animate();
            foreach (var s in UnityEngine.Object.FindObjectsByType<NitrateSway>(FindObjectsInactive.Exclude)) s.Animate();
            foreach (var f in UnityEngine.Object.FindObjectsByType<NitrateFigureAnimator>(FindObjectsInactive.Exclude)) f.Animate();
        }

        // The tonal fingerprint, compared with LIMBO stills (median of 11): 37% of pixels at 0-10, 42% outside 10..200,
        // a black mass along the bottom of about 18% of the frame height, about 70% flat 24 px blocks.
        static string Metrics(Texture2D tex)
        {
            Color32[] px = tex.GetPixels32();
            int w = tex.width, h = tex.height, n = px.Length;
            var bands = new int[8];
            int[] edges = { 10, 25, 50, 90, 140, 190, 230 };
            var luma = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int y = (px[i].r * 54 + px[i].g * 183 + px[i].b * 19) >> 8;
                luma[i] = (byte)y;
                int band = 0;
                while (band < 7 && y > edges[band]) band++;
                bands[band]++;
            }
            int bottomRows = 0; // rows from the bottom whose mean luma is <= 12
            for (int row = 0; row < h; row++)
            {
                long sum = 0;
                for (int x = 0; x < w; x++) sum += luma[row * w + x];
                if (sum / w > 12) break;
                bottomRows++;
            }
            int flat = 0, hard = 0, blocks = 0;
            for (int by = 0; by + 24 <= h; by += 24)
            for (int bx = 0; bx + 24 <= w; bx += 24)
            {
                int lo = 255, hi = 0;
                for (int y = by; y < by + 24; y++)
                for (int x = bx; x < bx + 24; x++)
                {
                    int v = luma[y * w + x];
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
                blocks++;
                if (hi - lo < 14) flat++;
                if (hi - lo >= 60) hard++;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append("bands%");
            foreach (int b in bands) sb.Append(' ').Append((100f * b / n).ToString("0.0"));
            sb.Append(" | black<=10 ").Append((100f * bands[0] / n).ToString("0.0")).Append('%');
            sb.Append(" | bottomBlack ").Append((100f * bottomRows / h).ToString("0.0")).Append('%');
            sb.Append(" | flat ").Append((100f * flat / blocks).ToString("0")).Append("% hard ").Append((100f * hard / blocks).ToString("0.0")).Append('%');
            return sb.ToString();
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
