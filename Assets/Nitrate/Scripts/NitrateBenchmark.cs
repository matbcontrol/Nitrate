using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nitrate
{
    /// <summary>
    /// Repeatable frame-budget benchmark. Every configuration replays the same 15 s camera loop (the hero shot),
    /// first a warm-up, then frame, GPU and CPU times are sampled and written to a CSV.
    /// A player started with -benchmark runs it and quits. Numbers are only meaningful in a player build.
    /// Cost of a layer = its "no_..." configuration minus the full one. Deltas are not strictly additive.
    /// </summary>
    public class NitrateBenchmark : MonoBehaviour
    {
        public NitrateControls controls;
        [Tooltip("Run when entering Play mode in the editor (for testing the script only).")]
        public bool runInEditor;
        public float initialWarmupSeconds = 5f;
        public float warmupSeconds = 2f;
        [Tooltip("One full camera loop, so every configuration sees the same frames.")]
        public float measureSeconds = NitrateClock.LoopSeconds;
        [Min(1)] public int passes = 3;

        struct Config
        {
            public string name;
            public bool low;
            public bool film, bloom, dof, atmosphere, hdr64, shadows, smaa;
            public BeamMode beams;
            public float renderScale; // 0: the preset's own
        }

        class Samples
        {
            public readonly List<float> frame = new List<float>(4096);
            public readonly List<float> gpu = new List<float>(4096);
            public readonly List<float> cpuMain = new List<float>(4096);
            public readonly List<float> cpuRender = new List<float>(4096);
            public void Clear() { frame.Clear(); gpu.Clear(); cpuMain.Clear(); cpuRender.Clear(); }
        }

        const float k_MaxSaneMs = 1000f;
        const float k_HitchMs = 25f;
        double m_NewestCpuMain, m_NewestGpu;

        readonly FrameTiming[] m_Timings = new FrameTiming[8];
        readonly StringBuilder m_Csv = new StringBuilder();
        ulong m_LastTimingStamp;
        bool m_Running;
        int m_RowsWritten, m_RunCount;
        string m_Status = "";
        string m_ShotFolder;
        bool m_QuitWhenDone;
        int m_VSyncWas;

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static Config Full(string name, bool low) => new Config
        {
            name = name, low = low, film = true, bloom = true, dof = true, atmosphere = true, hdr64 = true, shadows = true, smaa = true,
            beams = BeamMode.ShadowMarch
        };

        // One layer off at a time against the full look; the Low preset gets the layers that matter on an iGPU.
        static List<Config> BuildConfigs()
        {
            var list = new List<Config>();
            Config c;
            list.Add(Full("high", false));
            c = Full("high_no_beams", false); c.beams = BeamMode.Off; list.Add(c);
            c = Full("high_radial_beams", false); c.beams = BeamMode.RadialBlur; list.Add(c);
            c = Full("high_no_dof", false); c.dof = false; list.Add(c);
            c = Full("high_no_bloom", false); c.bloom = false; list.Add(c);
            c = Full("high_no_film", false); c.film = false; list.Add(c);
            c = Full("high_hdr32", false); c.hdr64 = false; list.Add(c);
            c = Full("high_no_atmosphere", false); c.atmosphere = false; c.beams = BeamMode.Off; list.Add(c);
            list.Add(Full("low", true));
            c = Full("low_no_beams", true); c.beams = BeamMode.Off; list.Add(c);
            c = Full("low_no_dof", true); c.dof = false; list.Add(c);
            c = Full("low_no_film", true); c.film = false; list.Add(c);
            // Where the base cost goes (the scene itself, before any of the layers above).
            c = Full("high_no_shadows", false); c.shadows = false; list.Add(c);
            c = Full("high_no_smaa", false); c.smaa = false; list.Add(c);
            c = Full("low_scale50", true); c.renderScale = 0.5f; list.Add(c);
            return list;
        }

        void Start()
        {
            bool requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-benchmark") >= 0;
            if (Application.isEditor)
                requested |= runInEditor;
            // Optional overrides: -passes 1 -measure 6 for a quick check, -shots <folder> to save a frame per configuration.
            if (int.TryParse(Arg("-passes"), out int p)) passes = Mathf.Max(1, p);
            if (float.TryParse(Arg("-measure"), NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) measureSeconds = m;
            m_ShotFolder = Arg("-shots");
            if (requested)
            {
                m_QuitWhenDone = true;
                StartCoroutine(Run());
            }
        }

        // F9 starts the same benchmark from inside the player, so no launcher script is needed. Started this way it
        // keeps the player open at the end and shows where the CSV went.
        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (!m_Running && keyboard != null && keyboard.f9Key.wasPressedThisFrame)
            {
                m_QuitWhenDone = false;
                StartCoroutine(Run());
            }
        }

        void OnDisable()
        {
            if (m_Running)
                Restore();
        }

        IEnumerator Run()
        {
            if (controls == null)
            {
                Debug.LogError("NitrateBenchmark: assign controls.");
                QuitPlayer(1);
                yield break;
            }
            if (!FrameTimingManager.IsFeatureEnabled())
                Debug.LogWarning("NitrateBenchmark: Frame Timing Stats is off (Player Settings), GPU/CPU columns will be n/a.");

            m_VSyncWas = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            m_Running = true;
            controls.hudVisible = false; // the HUD itself costs GPU time

            List<Config> configs = BuildConfigs();
            m_RunCount = configs.Count * passes;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None); // a log line must not cause a hitch itself
            m_Csv.Clear();
            WriteHeader();
            bool finished = false;

            try
            {
                Apply(configs[0]);
                NitrateClock.Restart();
                m_Status = "Benchmark: initial warm-up";
                yield return WaitRealtime(initialWarmupSeconds);

                var samples = new Samples();
                for (int run = 0; run < m_RunCount; run++)
                {
                    int pass = run / configs.Count + 1;
                    int i = run % configs.Count;
                    Config config = configs[pass % 2 == 0 ? configs.Count - 1 - i : i];
                    Apply(config);
                    m_Status = $"Benchmark pass {pass}/{passes}, {i + 1}/{configs.Count}: {config.name}";
                    Debug.Log(m_Status);
                    NitrateClock.Restart();
                    yield return WaitRealtime(warmupSeconds);
                    // A screenshot during the warm-up (never during measurement) to check what each configuration looks like.
                    if (pass == 1 && !string.IsNullOrEmpty(m_ShotFolder))
                    {
                        Directory.CreateDirectory(m_ShotFolder);
                        ScreenCapture.CaptureScreenshot(Path.Combine(m_ShotFolder, config.name + ".png"));
                        yield return null;
                        yield return null;
                    }

                    // No second restart here: it would snap the sails back mid-run. The loop is seamless, so 15 s of
                    // measurement from the end of the warm-up still cover exactly one loop, the same frames every time.
                    samples.Clear();
                    SkipPendingTimings();
                    int frames = 0;
                    int gcCount = GC.CollectionCount(0);
                    float start = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - start < measureSeconds)
                    {
                        yield return null;
                        frames++;
                        float frameMs = Time.unscaledDeltaTime * 1000f;
                        if (frameMs > 0f && frameMs < k_MaxSaneMs) samples.frame.Add(frameMs);
                        CollectTimings(samples);
                        // A frame long enough to see as a jerk: log when in the loop it happened and what was slow.
                        int gcNow = GC.CollectionCount(0);
                        if (frameMs > k_HitchMs)
                            Debug.Log($"HITCH {config.name} loop_t={NitrateClock.LoopPhase * NitrateClock.LoopSeconds:F2} frame_ms={frameMs:F1} " +
                                      $"gc={gcNow - gcCount} cpu_main_ms={m_NewestCpuMain:F1} gpu_ms={m_NewestGpu:F1}");
                        gcCount = gcNow;
                    }
                    AppendRow(pass, config.name, frames / (Time.realtimeSinceStartup - start), samples);
                }
                finished = true;
            }
            finally
            {
                Finish(finished);
            }
        }

        void Apply(Config c)
        {
            controls.ApplyPreset(c.low ? controls.low : controls.high);
            controls.SetFilm(c.film);
            controls.SetBloom(c.bloom);
            controls.SetDepthOfField(c.dof);
            controls.SetAtmosphere(c.atmosphere);
            controls.SetBeams(c.beams);
            controls.SetHdr64(c.hdr64);
            if (!c.smaa)
                controls.SetSmaa(false);
            if (c.renderScale > 0f && UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null)
                UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.renderScale = c.renderScale;
            Light moon = RenderSettings.sun;
            if (moon == null)
                foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                    if (light.type == LightType.Directional)
                        moon = light;
            if (moon != null)
            {
                m_MoonShadows ??= moon.shadows;
                moon.shadows = c.shadows ? m_MoonShadows.Value : LightShadows.None;
            }
        }

        LightShadows? m_MoonShadows;

        void Restore()
        {
            Apply(Full("restore", false));
            controls.hudVisible = true;
            QualitySettings.vSyncCount = m_VSyncWas;
            m_Running = false;
        }

        static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        void SkipPendingTimings()
        {
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings((uint)m_Timings.Length, m_Timings);
            for (int i = 0; i < count; i++)
                m_LastTimingStamp = Math.Max(m_LastTimingStamp, m_Timings[i].frameStartTimestamp);
        }

        // Takes every newly completed frame once (de-duplicated by its start timestamp).
        void CollectTimings(Samples samples)
        {
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings((uint)m_Timings.Length, m_Timings);
            ulong newest = m_LastTimingStamp;
            m_NewestCpuMain = m_NewestGpu = 0.0;
            for (int i = 0; i < count; i++)
            {
                FrameTiming t = m_Timings[i];
                if (t.frameStartTimestamp <= m_LastTimingStamp)
                    continue;
                newest = Math.Max(newest, t.frameStartTimestamp);
                AddSane(samples.gpu, t.gpuFrameTime);
                AddSane(samples.cpuMain, t.cpuMainThreadFrameTime);
                AddSane(samples.cpuRender, t.cpuRenderThreadFrameTime);
                m_NewestCpuMain = Math.Max(m_NewestCpuMain, t.cpuMainThreadFrameTime);
                m_NewestGpu = Math.Max(m_NewestGpu, t.gpuFrameTime);
            }
            m_LastTimingStamp = newest;
        }

        static void AddSane(List<float> list, double ms)
        {
            if (ms > 0.0 && ms < k_MaxSaneMs)
                list.Add((float)ms);
        }

        void Finish(bool finished)
        {
            Restore();
            if (!finished)
                m_Csv.AppendLine($"# status,incomplete ({m_RowsWritten}/{m_RunCount} runs)");
            string path = WriteCsv();
            m_Status = (finished ? "Benchmark done: " : "Benchmark stopped early: ") + path;
            Debug.Log(m_Status);
            if (m_QuitWhenDone)
                QuitPlayer(finished ? 0 : 1);
        }

        static void QuitPlayer(int exitCode)
        {
            if (!Application.isEditor)
                Application.Quit(exitCode);
        }

        static string Clean(string value) => value.Replace(',', ' ').Trim();

        void WriteHeader()
        {
            m_Csv.AppendLine("# Nitrate benchmark");
            m_Csv.AppendLine("# mode," + (Application.isEditor ? "editor (not representative)" : "player"));
            m_Csv.AppendLine("# gpu," + Clean(SystemInfo.graphicsDeviceName));
            m_Csv.AppendLine("# graphics_api," + SystemInfo.graphicsDeviceType);
            m_Csv.AppendLine("# cpu," + Clean(SystemInfo.processorType));
            m_Csv.AppendLine($"# resolution,{Screen.width}x{Screen.height}");
            m_Csv.AppendLine("# unity," + Application.unityVersion);
            m_Csv.AppendLine("# date," + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            m_Csv.AppendLine("# warmup_s," + F(warmupSeconds) + ",measure_s," + F(measureSeconds) + ",passes," + passes);
            m_Csv.AppendLine("pass,config,fps,frame_avg_ms,frame_p95_ms,frame_p99_ms,gpu_avg_ms,gpu_median_ms,gpu_p95_ms,gpu_p99_ms," +
                             "cpu_main_avg_ms,cpu_render_avg_ms,gpu_samples,frame_max_ms,hitches");
        }

        void AppendRow(int pass, string name, float fps, Samples s)
        {
            s.frame.Sort();
            s.gpu.Sort();
            s.cpuMain.Sort();
            s.cpuRender.Sort();
            m_Csv.Append(pass).Append(',').Append(name).Append(',').Append(F(fps))
                .Append(',').Append(Avg(s.frame)).Append(',').Append(Pct(s.frame, 0.95)).Append(',').Append(Pct(s.frame, 0.99))
                .Append(',').Append(Avg(s.gpu)).Append(',').Append(Pct(s.gpu, 0.5)).Append(',').Append(Pct(s.gpu, 0.95)).Append(',').Append(Pct(s.gpu, 0.99))
                .Append(',').Append(Avg(s.cpuMain)).Append(',').Append(Avg(s.cpuRender))
                .Append(',').Append(s.gpu.Count)
                .Append(',').Append(s.frame.Count > 0 ? F(s.frame[s.frame.Count - 1]) : "n/a")
                .Append(',').Append(Hitches(s.frame))
                .AppendLine();
            m_RowsWritten++;
        }

        // Frames that took more than twice the median: the ones you see as a jerk in the motion.
        static int Hitches(List<float> sorted)
        {
            if (sorted.Count == 0) return 0;
            float limit = 2f * sorted[sorted.Count / 2];
            int count = 0;
            for (int i = sorted.Count - 1; i >= 0 && sorted[i] > limit; i--) count++;
            return count;
        }

        static string Avg(List<float> values)
        {
            if (values.Count == 0) return "n/a";
            double sum = 0;
            foreach (float v in values) sum += v;
            return F(sum / values.Count);
        }

        static string Pct(List<float> sorted, double p)
        {
            if (sorted.Count == 0) return "n/a";
            int index = Mathf.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1);
            return F(sorted[index]);
        }

        static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        string WriteCsv()
        {
            string gpuName = Clean(SystemInfo.graphicsDeviceName).Replace(' ', '_');
            foreach (char invalid in Path.GetInvalidFileNameChars())
                gpuName = gpuName.Replace(invalid, '_');
            string file = $"benchmark_{gpuName}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.csv";
            string root = Path.GetDirectoryName(Application.dataPath);
            string folder = Application.isEditor ? Path.Combine(root, "Logs") : root;
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, file);
                File.WriteAllText(path, m_Csv.ToString());
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("NitrateBenchmark: could not write next to the executable (" + e.Message + ")");
                string path = Path.Combine(Application.persistentDataPath, file);
                File.WriteAllText(path, m_Csv.ToString());
                return path;
            }
        }

        void OnGUI()
        {
            if (!string.IsNullOrEmpty(m_Status))
                GUI.Label(new Rect(10f, Screen.height - 30f, 900f, 24f), m_Status);
        }
    }
}
