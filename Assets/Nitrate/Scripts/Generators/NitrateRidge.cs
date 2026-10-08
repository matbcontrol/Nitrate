using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// A strip of land seen from the side: a noisy top edge extruded in depth. Banks, the mill's hill, far ridges.
    /// Height is shaped along the length by a curve, so one component makes a bank, a mound or a mountain line.
    /// The fringe grows grass, thorns and reeds along the top in three depth rows, in clumps: in LIMBO no ground edge
    /// is ever a clean line, and that broken edge is most of what makes a silhouette read as organic.
    /// </summary>
    public class NitrateRidge : NitrateGenerator
    {
        public enum FringeStyle { Grass, Trees }

        public float length = 60f;
        public float depth = 6f;
        [Tooltip("Bottom of the strip (local y). Put it below the water.")]
        public float bottom = -2f;
        public float height = 1.5f;
        [Tooltip("Height multiplier along the length (0..1).")]
        public AnimationCurve profile = AnimationCurve.Constant(0f, 1f, 1f);
        [Tooltip("Noise wavelength in metres.")]
        public float wavelength = 12f;
        [Range(0f, 1f)] public float roughness = 0.45f;
        public float step = 0.5f;

        [Header("Fringe")]
        public bool fringe = true;
        [Tooltip("Grass: curved blades. Trees: bare flat trees (trunk and forked limbs) for far tree lines.")]
        public FringeStyle fringeStyle = FringeStyle.Grass;
        [Tooltip("Average distance between blades along the ridge, metres. Scale it with distance from the camera.")]
        public float fringeSpacing = 0.15f;
        [Tooltip("Blade height range, metres. Far ridges use big values: the blades then read as a tree line.")]
        public Vector2 fringeHeight = new Vector2(0.12f, 0.6f);
        [Tooltip("Wind lean shared by the whole fringe, degrees, plus random spread.")]
        public float fringeLean = -14f; // the wind blows toward -x, so the grass leans left
        [Range(0f, 1f)] public float fringeClumping = 0.55f;
        [Range(0f, 0.2f)] public float fringeExtras = 0.04f;
        [Tooltip("Blade height that bends fully in the wind. Shorter blades are stiffer, taller ones bend all the way.")]
        public float fringeWindHeight = 0.6f;

        /// <summary>Height of the top edge at a local x position (for standing things on the ridge).</summary>
        public float SampleTop(float localX)
        {
            float offset = Range(new System.Random(seed), 0f, 1000f);
            return Mathf.Max(Top(localX, offset), bottom + 0.01f);
        }

        float Top(float x, float offset)
        {
            float u = Mathf.Clamp01(x / length + 0.5f);
            float noise = 0f, amplitude = 1f, frequency = 1f / Mathf.Max(wavelength, 0.1f), total = 0f;
            for (int o = 0; o < 4; o++)
            {
                noise += Mathf.PerlinNoise(x * frequency + offset, offset * 0.37f + o * 11.3f) * amplitude;
                total += amplitude;
                amplitude *= roughness;
                frequency *= 2.1f;
            }
            return height * profile.Evaluate(u) * (noise / total * 1.6f - 0.3f);
        }

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            int columns = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(step, 0.05f)));
            float offset = Range(random, 0f, 1000f);
            var top = new float[columns + 1];
            for (int i = 0; i <= columns; i++)
                top[i] = Top((i / (float)columns - 0.5f) * length, offset);

            // Extrude the outline in depth: front face, top, back face, two end caps.
            float zf = -depth * 0.5f, zb = depth * 0.5f;
            for (int i = 0; i < columns; i++)
            {
                float x0 = (i / (float)columns - 0.5f) * length;
                float x1 = ((i + 1) / (float)columns - 0.5f) * length;
                float y0 = Mathf.Max(top[i], bottom + 0.01f), y1 = Mathf.Max(top[i + 1], bottom + 0.01f);
                builder.Quad(new Vector3(x0, bottom, zf), new Vector3(x0, y0, zf), new Vector3(x1, y1, zf), new Vector3(x1, bottom, zf));
                builder.Quad(new Vector3(x1, bottom, zb), new Vector3(x1, y1, zb), new Vector3(x0, y0, zb), new Vector3(x0, bottom, zb));
                builder.Quad(new Vector3(x0, y0, zf), new Vector3(x0, y0, zb), new Vector3(x1, y1, zb), new Vector3(x1, y1, zf));
            }
            float xl = -0.5f * length, xr = 0.5f * length;
            float yl = Mathf.Max(top[0], bottom + 0.01f), yr = Mathf.Max(top[columns], bottom + 0.01f);
            builder.Quad(new Vector3(xl, bottom, zb), new Vector3(xl, yl, zb), new Vector3(xl, yl, zf), new Vector3(xl, bottom, zf));
            builder.Quad(new Vector3(xr, bottom, zf), new Vector3(xr, yr, zf), new Vector3(xr, yr, zb), new Vector3(xr, bottom, zb));

            if (fringe)
                BuildFringe(builder, random, offset, zf, zb);
        }

        // Blades are flat tapered strips facing the camera (-z), bent by a shared wind, standing in clumps.
        void BuildFringe(NitrateMeshBuilder builder, System.Random random, float offset, float zf, float zb)
        {
            float spacing = Mathf.Max(fringeSpacing, 0.02f);
            float[] rows = { zf + 0.02f, Mathf.Lerp(zf, zb, 0.4f), Mathf.Lerp(zf, zb, 0.8f) };
            float[] rowScale = { 1f, 1.25f, 1.5f }; // rear rows a little taller, so all three show above the front one
            for (int r = 0; r < rows.Length; r++)
            {
                for (float x = -0.5f * length; x < 0.5f * length; x += spacing * Range(random, 0.6f, 1.4f))
                {
                    float clump = Mathf.PerlinNoise(x * 0.35f + offset + r * 7.1f, seed * 0.13f + r);
                    if (clump < fringeClumping * 0.6f)
                        continue;
                    bool trees = fringeStyle == FringeStyle.Trees;
                    int blades = clump < 0.6f ? 1 : random.Next(2, trees ? 3 : 5);
                    float ground = Mathf.Max(Top(x, offset), bottom + 0.01f) - 0.03f;
                    for (int b = 0; b < blades; b++)
                    {
                        float bx = x + Range(random, -0.4f, 0.4f) * spacing;
                        float t = (float)random.NextDouble();
                        float h = Mathf.Lerp(fringeHeight.x, fringeHeight.y, t * t * t) * rowScale[r] * (0.6f + clump);
                        float lean = fringeLean + Range(random, -25f, 25f);
                        float roll = (float)random.NextDouble();
                        builder.phase = (float)random.NextDouble();
                        float tipFlex = Mathf.Clamp01(h / Mathf.Max(fringeWindHeight, 0.01f));
                        if (trees)
                            FarTree(builder, random, new Vector3(bx, ground, rows[r]), h, tipFlex);
                        else if (roll < fringeExtras)
                            Thorns(builder, random, new Vector3(bx, ground, rows[r]), h, tipFlex * 0.4f);
                        else
                            Blade(builder, new Vector3(bx, ground, rows[r]), h, 0.008f + 0.03f * h, lean, tipFlex);
                    }
                }
            }
        }

        static void Blade(NitrateMeshBuilder builder, Vector3 root, float h, float halfWidth, float leanDegrees, float tipFlex)
        {
            // Four segments; the lean grows toward the tip, so the blade curves instead of tilting.
            const int segments = 4;
            float lean = leanDegrees * Mathf.Deg2Rad;
            Vector3 prevL = root + Vector3.left * halfWidth, prevR = root + Vector3.right * halfWidth;
            Vector3 point = root;
            for (int s = 1; s <= segments; s++)
            {
                float t = s / (float)segments;
                float a = lean * t * t;
                point += new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * (h / segments);
                float w = halfWidth * (1f - t);
                Vector3 l = point + Vector3.left * w, r = point + Vector3.right * w;
                builder.Quad(prevL, l, r, prevR, tipFlex * (s - 1) / segments, tipFlex * t);
                prevL = l;
                prevR = r;
            }
        }

        // A bare tree as flat as the blades: a straight trunk, limbs angled up with a fork at the end, twigs on top.
        // Far away, fog and blur leave only the outline, and these proportions are what make an outline read as a tree.
        static void FarTree(NitrateMeshBuilder builder, System.Random random, Vector3 root, float h, float flex)
        {
            float lean = Range(random, -4f, 4f) * Mathf.Deg2Rad;
            var up = new Vector3(Mathf.Sin(lean), Mathf.Cos(lean), 0f);
            float trunk = 0.02f * h + 0.06f;
            Strip(builder, root, root + up * h, trunk, trunk * 0.25f, 0f, flex * 0.3f);

            int limbs = random.Next(3, 7);
            float side = random.NextDouble() < 0.5 ? -1f : 1f;
            for (int i = 0; i < limbs; i++, side = -side)
            {
                float t = Range(random, 0.35f, 0.92f);
                Vector3 start = root + up * (h * t);
                float angle = lean + side * Range(random, 25f, 60f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
                float length = h * Range(random, 0.15f, 0.35f) * (1.2f - 0.6f * t);
                float width = trunk * Mathf.Lerp(1f, 0.25f, t) * 0.6f;
                Vector3 end = start + dir * length;
                Strip(builder, start, end, width, width * 0.3f, flex * 0.3f * t, flex * 0.6f);
                // The fork turns back toward the sky.
                float forkAngle = angle - side * Range(random, 15f, 35f) * Mathf.Deg2Rad;
                Strip(builder, end, end + new Vector3(Mathf.Sin(forkAngle), Mathf.Cos(forkAngle), 0f) * (length * Range(random, 0.4f, 0.7f)),
                      width * 0.3f, width * 0.05f, flex * 0.6f, flex);
            }
            int twigs = random.Next(2, 4);
            for (int i = 0; i < twigs; i++)
            {
                float angle = lean + Range(random, -35f, 35f) * Mathf.Deg2Rad;
                Vector3 start = root + up * (h * Range(random, 0.85f, 0.98f));
                Strip(builder, start, start + new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * (h * Range(random, 0.08f, 0.18f)),
                      trunk * 0.2f, trunk * 0.03f, flex * 0.3f, flex);
            }
        }

        // A tapered flat strip from a to b, facing the camera, wound like the blades.
        static void Strip(NitrateMeshBuilder builder, Vector3 a, Vector3 b, float widthA, float widthB, float flexA, float flexB)
        {
            Vector3 d = (b - a).normalized;
            var left = new Vector3(-d.y, d.x, 0f);
            builder.Quad(a + left * widthA, b + left * widthB, b - left * widthB, a - left * widthA, flexA, flexB);
        }

        static void Thorns(NitrateMeshBuilder builder, System.Random random, Vector3 root, float h, float tipFlex)
        {
            int spikes = random.Next(6, 11);
            for (int i = 0; i < spikes; i++)
            {
                float a = Range(random, -70f, 70f);
                Blade(builder, root, h * Range(random, 0.5f, 1.1f), 0.012f, a, tipFlex);
            }
        }
    }
}
