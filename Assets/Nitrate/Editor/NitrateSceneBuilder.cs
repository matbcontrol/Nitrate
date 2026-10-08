using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nitrate.EditorTools
{
    /// <summary>
    /// Sets the project up and builds the Mill scene from the procedural kit, with the composition used in the hero shot.
    /// Every number here is a composition decision; the comments say what it is for.
    /// </summary>
    public static class NitrateSceneBuilder
    {
        const string k_Root = "Assets/Nitrate";
        const string k_Materials = k_Root + "/Materials";
        const string k_Settings = k_Root + "/Settings";
        const string k_ScenePath = k_Root + "/Scenes/Mill.unity";
        const string k_BlueNoisePath = k_Root + "/Textures/BlueNoise64.png";

        public static UniversalRenderPipelineAsset PipelineAsset =>
            (UniversalRenderPipelineAsset)(QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline);

        public static UniversalRendererData RendererData => (UniversalRendererData)PipelineAsset.rendererDataList[0];

        [MenuItem("Nitrate/1. Configure Project")]
        public static void ConfigureProject()
        {
            EnsureFolder(k_Materials);
            EnsureFolder(k_Settings);
            EnsureFolder(k_Root + "/Scenes");

            // URP asset: HDR 64-bit (the film pass dithers before the 8-bit output; R11G11B10 would swallow it),
            // no MSAA (fog and grain hide aliasing, SMAA does the rest), one hard shadow cascade reaching past the mill.
            var asset = new SerializedObject(PipelineAsset);
            asset.FindProperty("m_SupportsHDR").boolValue = true;
            asset.FindProperty("m_HDRColorBufferPrecision").intValue = (int)HDRColorBufferPrecision._64Bits;
            asset.FindProperty("m_MSAA").intValue = 1;
            asset.FindProperty("m_RenderScale").floatValue = 1f;
            asset.FindProperty("m_RequireDepthTexture").boolValue = true;
            asset.FindProperty("m_RequireOpaqueTexture").boolValue = false;
            asset.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            asset.FindProperty("m_ShadowCascadeCount").intValue = 1;
            asset.FindProperty("m_ShadowDistance").floatValue = 140f;
            asset.FindProperty("m_SoftShadowsSupported").boolValue = false;
            // GPU Resident Drawer off: with about 40 objects it saves nothing, and it uploads transforms only at frame
            // boundaries, so a render request made right after moving something would still see the old pose.
            asset.FindProperty("m_GPUResidentDrawerMode").intValue = 0;
            asset.ApplyModifiedPropertiesWithoutUndo();

            // Renderer: depth priming forced (the scene needs a depth texture anyway; the opaque pass then shades each
            // pixel once). SSAO is REMOVED, not just switched off: every surface is black, so there is nothing to occlude,
            // and a disabled SSAO feature gets its shaders stripped from the player, which then throws while creating
            // the pipeline and renders a black screen.
            UniversalRendererData data = RendererData;
            data.depthPrimingMode = DepthPrimingMode.Forced;
            RemoveFeature<ScreenSpaceAmbientOcclusion>(data);

            Material atmosphere = GetOrCreateMaterial("NitrateAtmosphere", "Nitrate/Atmosphere", ApplyAtmosphereLook);
            // The film print and the silhouettes are Shader Graphs (Shaders/Graphs); Shaders/NitrateFilm.shader and
            // Shaders/NitrateSilhouette.shader are the same code written by hand, kept as the readable reference.
            Material film = GetOrCreateMaterial("NitrateFilm", k_FilmShader, m =>
            {
                m.shader = Shader.Find(k_FilmShader);
                ApplyFilmLook(m);
            });
            AddFeature<NitrateAtmosphereFeature>(data, "Nitrate Atmosphere").material = atmosphere;
            AddFeature<NitrateFilmFeature>(data, "Nitrate Film").material = film;
            EditorUtility.SetDirty(data);

            // Every surface: pure black, casts shadows, moved by the wind in all its passes (shadow and depth included,
            // so the beams cut by a swaying branch sway with it).
            GetOrCreateMaterial("Silhouette", k_SilhouetteShader, m =>
            {
                m.shader = Shader.Find(k_SilhouetteShader);
                m.SetColor("_BaseColor", Color.black);
                m.SetFloat("_WindResponse", 1f);
                m.enableInstancing = true;
            });
            GetOrCreateMaterial("LitGrey", "Universal Render Pipeline/Simple Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f));
                m.SetFloat("_Smoothness", 0f);
                m.SetFloat("_SpecularHighlights", 0f);
                m.DisableKeyword("_SPECULAR_COLOR");
            });
            GetOrCreateMaterial("LanternBulb", "Universal Render Pipeline/Unlit", m =>
            {
                m.SetColor("_BaseColor", new Color(2.6f, 1.45f, 0.55f, 1f)); // HDR: above the bloom threshold, so it glows
            });

            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.runInBackground = true; // an unfocused window must not throttle a benchmark

            // Present on the display's refresh. Without vsync, hundreds of frames a second tear the turning sails into
            // slices, and an iGPU hovering near 60 fps judders. The benchmark switches it off while it measures.
            int quality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.vSyncCount = 1;
            }
            QualitySettings.SetQualityLevel(quality, false);
            PlayerSettings.productName = "Nitrate";
            AssetDatabase.SaveAssets();
            Debug.Log("Nitrate: project configured.");
        }

        [MenuItem("Nitrate/2. Build Mill Scene")]
        public static void BuildScene()
        {
            ConfigureProject();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting: no ambient, no sky, no built-in fog. Every pixel of tone comes from the Atmosphere pass.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0f;
            var lighting = GetOrCreateAsset(k_Settings + "/NitrateLighting.lighting", () => new LightingSettings());
            lighting.bakedGI = false;
            lighting.realtimeGI = false;
            Lightmapping.lightingSettings = lighting;

            Material silhouette = AssetDatabase.LoadAssetAtPath<Material>(k_Materials + "/Silhouette.mat");
            Material bulb = AssetDatabase.LoadAssetAtPath<Material>(k_Materials + "/LanternBulb.mat");

            // ECLIPSE layout. Water level is y = 0. The moon is 6.45 degrees above the horizon, straight ahead (+z).
            // The camera's view axis runs from the camera through the figure's plane to the mill's hub and on to the moon,
            // so the four sails are an X across the moon disc, and the dolly zoom slides along that same axis.
            var moonGo = new GameObject("Moon");
            Light moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.intensity = 1f;
            moon.color = new Color(0.85f, 0.9f, 1f);
            moon.shadows = LightShadows.Hard;
            moon.shadowStrength = 1f;
            moon.shadowBias = 0.3f;
            moon.shadowNormalBias = 0.2f;
            moonGo.transform.rotation = Quaternion.Euler(6.45f, 180f, 0f);

            Transform set = new GameObject("Set").transform;

            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.name = "Water";
            water.transform.SetParent(set, false);
            water.transform.position = new Vector3(0f, 0f, 100f);
            water.transform.localScale = new Vector3(150f, 1f, 60f);
            water.GetComponent<MeshRenderer>().sharedMaterial = silhouette;
            water.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // The dike the Lamplighter stands on, 59 m in front of the mill: one pure-black mass along the bottom of the frame.
            // It is 16 m deep and the figure stands near its back edge, so the wide end of the zoom looks down on black ground.
            Transform near = new GameObject("Near").transform;
            near.SetParent(set, false);
            var dike = Make<NitrateRidge>("Dike", near, new Vector3(-6f, 0f, -62.9f), silhouette, r => { r.length = 80f; r.depth = 16f; r.height = 1.2f; r.bottom = -6f; r.wavelength = 7f; r.roughness = 0.55f; r.seed = 3; r.fringeSpacing = 0.12f; r.fringeHeight = new Vector2(0.12f, 0.6f); r.fringeExtras = 0.05f; });
            NoShadows(dike);
            float figureX = -7.4f;
            float dikeLift = 2.1f - dike.SampleTop(figureX - dike.transform.position.x);
            dike.transform.position += Vector3.up * dikeLift; // the figure's feet end up at y = 2.1
            float DikeTop(float x) => dike.transform.position.y + dike.SampleTop(x - dike.transform.position.x);

            // The Lamplighter: a root at his feet with an animator, a Body part (pivot at the feet), an Arms part with the
            // pole (pivot at the shoulder, so he can lift the lantern), a scarf that flutters, a lantern that keeps hanging
            // straight down, and one warm eye. 1.75 m to the top of the hat: 14% of the frame height.
            var figure = new GameObject("Lamplighter").transform;
            figure.SetParent(near, false);
            figure.position = new Vector3(figureX, DikeTop(figureX), -58.92f);
            var body = Make<NitrateFigure>("Body", figure, Vector3.zero, silhouette, f => f.part = NitrateFigure.Part.Body);
            var arms = Make<NitrateFigure>("Arms", body.transform, NitrateFigure.ShoulderPivot, silhouette, f => f.part = NitrateFigure.Part.Arms);
            var animator = figure.gameObject.AddComponent<NitrateFigureAnimator>();
            animator.body = body.transform;
            animator.arms = arms.transform;
            var scarf = Make<NitrateFigure>("Scarf", body.transform, NitrateFigure.ScarfRoot, silhouette, f => f.part = NitrateFigure.Part.Scarf);
            var scarfSway = scarf.gameObject.AddComponent<NitrateSway>();
            scarfSway.axis = Vector3.forward;
            scarfSway.amplitude = 6f;
            scarfSway.period = 1.875f;
            scarfSway.windInfluence = 1f;
            scarfSway.windBias = -15f; // gusts lift the scarf
            var lantern = Make<NitrateLantern>("Lantern", arms.transform, NitrateFigure.PoleTipFromShoulder, silhouette, null);
            var sway = lantern.gameObject.AddComponent<NitrateSway>();
            sway.axis = Vector3.forward;
            sway.amplitude = 7f;
            sway.period = 3.75f;
            sway.worldSpace = true;
            sway.windInfluence = 0.5f;
            sway.windBias = -4f; // gusts push the lantern downwind
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.name = "Eye";
            eye.transform.SetParent(body.transform, false);
            eye.transform.localPosition = NitrateFigure.Eye;
            eye.transform.localScale = Vector3.one * 0.022f;
            eye.GetComponent<MeshRenderer>().sharedMaterial = bulb;
            eye.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var bulbGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(bulbGo.GetComponent<Collider>());
            bulbGo.name = "Bulb";
            bulbGo.transform.SetParent(lantern.transform, false);
            bulbGo.transform.localPosition = lantern.BulbPosition;
            bulbGo.transform.localScale = Vector3.one * 0.09f;
            var bulbRenderer = bulbGo.GetComponent<MeshRenderer>();
            bulbRenderer.sharedMaterial = bulb;
            bulbRenderer.shadowCastingMode = ShadowCastingMode.Off;

            foreach (var (x, seed) in new[] { (-17f, 11), (-1.5f, 12), (9f, 13) })
                MakeReeds("Reeds " + seed, near, new Vector3(x, DikeTop(x) - 0.1f, -58.6f), seed, new Vector2(2f, 0.8f), new Vector2(0.8f, 1.8f), silhouette);

            // Close to the camera: a giant trunk at the left edge, its crown arching over the top-left corner. At the
            // telephoto end it frames the shot; the camera passes it during the dolly zoom, so it flies out of frame (parallax you can't fake).
            var trunk = Make<NitrateTree>("Giant Trunk", near, new Vector3(-4.0f, -1f, -92f), silhouette, t => { t.height = 24f; t.baseRadius = 0.75f; t.levels = 3; t.lean = 2f; t.seed = 21; t.thorns = 30; t.roots = 0; t.stubs = 6; t.strands = 0.08f; t.windFlex = 0.2f; });
            NoShadows(trunk);

            // Middle ground, 25-35 m behind the figure: an islet with a cluster of dead trees. Fog lifts them to dark grey.
            Transform mid = new GameObject("Middle").transform;
            mid.SetParent(set, false);
            var islet = Make<NitrateRidge>("Islet", mid, new Vector3(-15f, 0f, -28f), silhouette, r => { r.length = 34f; r.depth = 8f; r.height = 1.4f; r.bottom = -2f; r.wavelength = 6f; r.seed = 31; r.fringeSpacing = 0.22f; r.fringeHeight = new Vector2(0.3f, 1.2f); r.profile = new AnimationCurve(new Keyframe(0f, -1f), new Keyframe(0.2f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(0.8f, 0.6f), new Keyframe(1f, -1f)); });
            NoShadows(islet);
            float IsletTop(float x) => islet.SampleTop(x - islet.transform.position.x);
            MakeTree("Tree A", mid, new Vector3(-17f, IsletTop(-17f) - 0.3f, -29f), 17f, 3, silhouette);
            MakeTree("Tree B", mid, new Vector3(-14.5f, IsletTop(-14.5f) - 0.3f, -27f), 13f, 7, silhouette); // its trunk must not stand right behind the figure
            MakeTree("Tree C", mid, new Vector3(-21f, IsletTop(-21f) - 0.3f, -25.5f), 10f, 11, silhouette);

            // The mill on the far shore. The hub is at (0, 15, 0), exactly on the view axis.
            Transform far = new GameObject("Far").transform;
            far.SetParent(set, false);
            NoShadows(Make<NitrateRidge>("Far Shore", far, new Vector3(0f, 0f, 8f), silhouette, r => { r.length = 400f; r.depth = 12f; r.height = 1.6f; r.bottom = -1f; r.wavelength = 9f; r.seed = 41; r.fringeSpacing = 0.3f; r.fringeHeight = new Vector2(0.4f, 1.6f); }));
            Make<NitrateRidge>("Mill Mound", far, new Vector3(0f, 0f, 3f), silhouette, r => { r.length = 36f; r.depth = 14f; r.height = 2.4f; r.bottom = -1f; r.wavelength = 14f; r.seed = 33; r.fringeSpacing = 0.22f; r.fringeHeight = new Vector2(0.3f, 1f); r.profile = new AnimationCurve(new Keyframe(0f, -1f), new Keyframe(0.25f, 0.5f), new Keyframe(0.5f, 1f), new Keyframe(0.75f, 0.5f), new Keyframe(1f, -1f)); });
            var mill = Make<NitrateMill>("Mill", far, new Vector3(0f, 0f, 0f), silhouette, null);
            mill.transform.position = new Vector3(0f, 15f, 0f) - mill.HubPosition;
            var sailsPivot = new GameObject("Sails Pivot");
            sailsPivot.transform.SetParent(mill.transform, false);
            sailsPivot.transform.localPosition = mill.HubPosition + Vector3.back * 0.5f;
            var rotor = sailsPivot.AddComponent<NitrateRotor>();
            rotor.axis = Vector3.forward;
            rotor.degreesPerLoop = 360f; // one turn per loop; the X pattern returns every 3.75 s
            rotor.phase = 45f;           // frame 1 is the X across the moon
            Make<NitrateSails>("Sails", sailsPivot.transform, Vector3.zero, silhouette, s => { s.length = 12.5f; });

            NoShadows(Make<NitrateRidge>("Tree Line", far, new Vector3(0f, 0f, 80f), silhouette, r => { r.length = 500f; r.depth = 20f; r.height = 9f; r.bottom = -1f; r.wavelength = 18f; r.roughness = 0.65f; r.seed = 51; r.fringeStyle = NitrateRidge.FringeStyle.Trees; r.fringeSpacing = 3.2f; r.fringeHeight = new Vector2(3.5f, 7f); r.fringeClumping = 0.5f; r.fringeWindHeight = 40f; }));
            NoShadows(Make<NitrateRidge>("Hills", far, new Vector3(0f, 0f, 250f), silhouette, r => { r.length = 900f; r.depth = 30f; r.height = 30f; r.bottom = -1f; r.wavelength = 120f; r.seed = 57; r.fringeStyle = NitrateRidge.FringeStyle.Trees; r.fringeSpacing = 6f; r.fringeHeight = new Vector2(6f, 12f); r.fringeClumping = 0.5f; r.fringeWindHeight = 80f; }));

            // Camera rig. The pivot is where the view axis (camera -> hub -> moon) crosses the figure's plane.
            // The dolly zoom keeps 12.5 m of that plane in frame, so the 1.75 m figure stays 14% of the frame height.
            var pivot = new GameObject("Camera Pivot");
            pivot.transform.position = new Vector3(0f, 15f, 0f) - Quaternion.Euler(-6.45f, 0f, 0f) * Vector3.forward * 60f;
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2000f;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.Low;
            camData.dithering = false; // the film pass dithers, after all post-processing
            var dolly = camGo.AddComponent<NitrateDollyZoom>();
            dolly.pivot = pivot.transform;
            dolly.frameHeight = 12.5f;
            dolly.pitch = 6.45f;
            dolly.yaw = 0f;
            dolly.lensShift = new Vector2(-0.1667f, -0.2f); // shifting the lens left and down puts the hub on the upper-right thirds point
            dolly.fovRange = new Vector2(15f, 38f);

            // Post-processing volume: bloom only above 1.5 (moon and lantern), depth of field on the figure.
            var volumeGo = new GameObject("Post Volume");
            Volume volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = BuildVolumeProfile(dolly.Distance);

            // Atmosphere scene state. Beams live between the figure's plane and the mill.
            var atmosphereGo = new GameObject("Atmosphere");
            var atmosphere = atmosphereGo.AddComponent<NitrateAtmosphere>();
            atmosphere.moon = moon;
            atmosphere.fogPlane = figure;
            atmosphere.viewCamera = cam;
            atmosphere.lantern = bulbGo.transform;
            atmosphere.beamBoxCenter = new Vector3(0f, 22.5f, -25f);
            atmosphere.beamBoxSize = new Vector3(140f, 45f, 68f);
            atmosphere.blueNoise = LoadBlueNoise();

            // Controls, moon drag, benchmark.
            var controlsGo = new GameObject("Controls");
            var controls = controlsGo.AddComponent<NitrateControls>();
            controls.rendererData = RendererData;
            controls.postVolume = volume;
            controls.filmMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_Materials + "/NitrateFilm.mat");
            controls.silhouetteMaterial = silhouette;
            controls.litMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_Materials + "/LitGrey.mat");
            controls.mainCamera = cam;
            controlsGo.AddComponent<NitrateMoonDrag>().moon = moon;
            controlsGo.AddComponent<NitrateBenchmark>().controls = controls;

            dolly.Apply();
            atmosphere.Apply();

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_ScenePath, true) };
            Debug.Log("Nitrate: scene built at " + k_ScenePath);
        }

        // The look, in numbers. Tuned against the reference "tonal fingerprint": about 40% of the frame near black,
        // a mid-grey atmosphere, a hard vignette and only two things brighter than white (the moon and the lantern).
        public static void ApplyAtmosphereLook(Material m)
        {
            m.SetColor("_HorizonColor", new Color(0.12f, 0.12f, 0.12f));
            m.SetColor("_ZenithColor", new Color(0.015f, 0.015f, 0.015f));
            m.SetColor("_NadirColor", new Color(0.02f, 0.02f, 0.02f));
            m.SetVector("_SkyGradient", new Vector4(-0.02f, 0.45f, 0f, 0f));
            m.SetVector("_GlowWide", new Vector4(0.45f, 6f, 0f, 0f));
            m.SetVector("_GlowTight", new Vector4(1.2f, 2.4f, 0f, 0f));
            m.SetFloat("_GlowOnFog", 0.35f);
            m.SetFloat("_MoonRadius", 1.35f);
            m.SetFloat("_MoonIntensity", 6f);
            m.SetFloat("_FogDistance", 120f);
            m.SetFloat("_FogGamma", 1.6f);
            m.SetFloat("_FogMax", 0.95f);
            m.SetFloat("_MistAmount", 0.15f);
            m.SetFloat("_MistHeight", 2.5f);
            m.SetFloat("_BeamDensity", 0.004f);
            m.SetFloat("_BeamHeightFalloff", 25f);
            m.SetFloat("_BeamAnisotropy", 0.7f);
            m.SetFloat("_BeamIntensity", 0.6f);
            m.SetFloat("_BeamMaxDistance", 200f);
            m.SetFloat("_BeamFloor", 0.03f);
            m.SetFloat("_BeamGain", 1.3f);
            m.SetFloat("_BlurDepthTolerance", 0.1f);
            m.SetFloat("_HeroMask", 4f);
            m.SetFloat("_LanternScatter", 0.03f);
            m.SetFloat("_BlurFar1", 250f); // the mill (about 65 m behind the figure) stays nearly sharp: it is the hero outline
            m.SetFloat("_BlurFar2", 700f);
            m.SetFloat("_BlurSky", 0.4f);
            m.SetFloat("_WaterLevel", 0f);
            m.SetFloat("_WaterReflect", 0.9f);
            m.SetFloat("_Ripple", 0.09f);
            m.SetFloat("_Motes", 1f);
        }

        public static void ApplyFilmLook(Material m)
        {
            m.SetFloat("_Exposure", 1f);
            m.SetFloat("_BlackPoint", 0.05f);
            m.SetFloat("_WhitePoint", 0.92f);
            m.SetFloat("_Gamma", 1.25f);
            m.SetFloat("_Contrast", 0.35f);
            m.SetFloat("_Ember", 1f);
            m.SetFloat("_EmberThreshold", 0.3f);
            m.SetFloat("_VignetteStrength", 0.92f);
            m.SetFloat("_VignetteInner", 0.3f);
            m.SetFloat("_VignetteOuter", 1.05f);
            m.SetFloat("_GrainAmount", 0.04f);
            m.SetFloat("_GrainSize", 2f);
            m.SetFloat("_Flicker", 0.015f);
            m.SetFloat("_Weave", 0.35f);
            m.SetFloat("_Dust", 0.5f);
        }

        static VolumeProfile BuildVolumeProfile(float focusDistance)
        {
            string path = k_Settings + "/NitrateVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            // No tonemapping: the film pass owns the tone curve. Neutral would also bleach the lantern's colour.
            var tonemapping = AddOverride<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.None);

            var bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(1.5f); // only the moon disc and the lantern; the fog never blooms, so blacks stay black
            bloom.intensity.Override(0.3f);
            bloom.scatter.Override(0.55f);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(4);

            // URP depth of field is off: the Atmosphere pass does a depth blur ladder instead (cheaper, and far
            // layers can blur far more than Bokeh's 14-pixel cap).
            var dof = AddOverride<DepthOfField>(profile);
            dof.active = false;
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(focusDistance);
            dof.focalLength.Override(70f);
            dof.aperture.Override(2.8f);
            dof.bladeCount.Override(6);
            dof.gaussianStart.Override(25f);
            dof.gaussianEnd.Override(110f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static Texture2D LoadBlueNoise()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_BlueNoisePath);
            return texture != null ? texture : NitrateBlueNoise.Generate(k_BlueNoisePath);
        }

        static T Make<T>(string name, Transform parent, Vector3 localPosition, Material material, System.Action<T> setup) where T : NitrateGenerator
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            T generator = go.AddComponent<T>();
            setup?.Invoke(generator);
            generator.Rebuild();
            return generator;
        }

        // Background land casts no shadows: its shadow volumes would swallow the beams near the ground,
        // and the edge of the shadow map would show up as straight lines in the fog.
        static void NoShadows(Component c) => c.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        static void MakeTree(string name, Transform parent, Vector3 position, float height, int seed, Material material)
        {
            Make<NitrateTree>(name, parent, position, material, t => { t.height = height; t.seed = seed; t.baseRadius = 0.14f + height * 0.012f; });
        }

        static void MakeReeds(string name, Transform parent, Vector3 position, int seed, Vector2 area, Vector2 heights, Material material)
        {
            var reeds = Make<NitrateReeds>(name, parent, position, material, r => { r.seed = seed; r.area = area; r.heightRange = heights; r.count = 36; });
            reeds.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        const string k_SilhouetteShader = "Nitrate/Graphs/NitrateSilhouette";
        const string k_FilmShader = "Nitrate/Graphs/NitrateFilm";

        static Material GetOrCreateMaterial(string name, string shaderName, System.Action<Material> setup)
        {
            string path = k_Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(material, path);
            }
            setup?.Invoke(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static T GetOrCreateAsset<T>(string path, System.Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = create();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        static T AddFeature<T>(UniversalRendererData data, string name) where T : ScriptableRendererFeature
        {
            foreach (ScriptableRendererFeature f in data.rendererFeatures)
                if (f is T existing)
                    return existing;

            T feature = ScriptableObject.CreateInstance<T>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            var so = new SerializedObject(data);
            SerializedProperty list = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
            return feature;
        }

        static void RemoveFeature<T>(UniversalRendererData data) where T : ScriptableRendererFeature
        {
            var so = new SerializedObject(data);
            SerializedProperty list = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (!(list.GetArrayElementAtIndex(i).objectReferenceValue is T feature))
                    continue;
                list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                list.DeleteArrayElementAtIndex(i);
                map.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, true);
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
