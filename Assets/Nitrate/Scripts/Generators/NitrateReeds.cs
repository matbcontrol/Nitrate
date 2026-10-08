using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// A clump of reeds: thin three-sided blades that curve as they rise, some with a cattail head.
    /// Real geometry instead of alpha-tested cards: no overdraw from transparent pixels and crisp silhouettes.
    /// Reeds in front of the figure should not cast shadows (Mesh Renderer > Cast Shadows = Off): they are thinner
    /// than a shadow-map texel and would sparkle in the beams.
    /// </summary>
    public class NitrateReeds : NitrateGenerator
    {
        public int count = 40;
        [Tooltip("Width (x) and depth (z) of the clump, metres.")]
        public Vector2 area = new Vector2(2.5f, 1f);
        public Vector2 heightRange = new Vector2(0.9f, 2f);
        [Tooltip("Lean of each blade, degrees.")]
        public float lean = 14f;
        [Tooltip("How much a blade curves toward its tip.")]
        public float bend = 0.35f;
        public float bladeRadius = 0.02f;
        [Range(0f, 1f)] public float cattails = 0.2f;

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            const int segments = 5;
            var points = new Vector3[segments + 1];
            var radii = new float[segments + 1];
            var flex = new float[segments + 1];

            for (int i = 0; i < count; i++)
            {
                // Denser in the middle of the clump.
                float u = Range(random, -1f, 1f), v = Range(random, -1f, 1f);
                Vector3 root = new Vector3(u * Mathf.Abs(u) * area.x * 0.5f, 0f, v * area.y * 0.5f);
                float height = Range(random, heightRange) * (1f - 0.35f * Mathf.Abs(u));
                Quaternion tilt = Quaternion.Euler(Range(random, -lean, lean), 0f, Range(random, -lean, lean) + u * lean);
                Vector3 bendDir = new Vector3(Range(random, -1f, 1f), 0f, Range(random, -0.3f, 0.3f)).normalized;

                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments;
                    Vector3 straight = tilt * (Vector3.up * height * t);
                    points[s] = root + straight + bendDir * (bend * height * t * t);
                    radii[s] = bladeRadius * (1f - t * 0.9f);
                    flex[s] = t * Mathf.Clamp01(height / 1.2f);
                }
                builder.phase = (float)random.NextDouble();
                builder.Tube(points, radii, 3, false, false, false, flex);

                if (Range(random, 0f, 1f) < cattails)
                {
                    Vector3 a = Vector3.Lerp(points[segments - 2], points[segments - 1], 0.3f);
                    Vector3 b = Vector3.Lerp(points[segments - 1], points[segments], 0.4f);
                    float headFlex = flex[segments - 1];
                    builder.Tube(new[] { a, Vector3.Lerp(a, b, 0.2f), Vector3.Lerp(a, b, 0.8f), b },
                                 new[] { bladeRadius, bladeRadius * 2.2f, bladeRadius * 2.2f, bladeRadius }, 5, true, true, false,
                                 new[] { headFlex, headFlex, headFlex, headFlex });
                }
            }
        }
    }
}
