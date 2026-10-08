using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Four windmill sails in the local XY plane, turning around local Z, each one ruined differently:
    /// a tapering lattice with uneven bars, some bars missing, torn canvas in part of the bays, rags, one snapped sail.
    /// The sails turn one full turn per loop, so they do not need to be identical for the loop to be seamless.
    /// Canvas is emitted with both windings: the moon is behind the sails, so the shadow pass sees its back faces.
    /// Every feature stays wider than about 20 cm, a few shadow-map texels, or its shadow would flicker in the beams.
    /// </summary>
    public class NitrateSails : NitrateGenerator
    {
        public float length = 12.5f;
        [Tooltip("Distance from the hub where the lattice starts.")]
        public float innerRadius = 1.6f;
        public float frameWidth = 2.3f;
        [Tooltip("Lattice width at the tip as a fraction of the width at the hub.")]
        [Range(0.3f, 1f)] public float tipWidth = 0.6f;
        public int bars = 11;
        public float sparWidth = 0.38f;
        public float thickness = 0.14f;
        [Range(0f, 0.4f)] public float missingBars = 0.14f;
        [Range(0f, 1f)] public float canvas = 0.45f;
        [Tooltip("Index of the snapped sail (-1 for none).")]
        public int brokenSail = 2;
        [Range(1, 8)] public int sailCount = 4;

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            for (int i = 0; i < sailCount; i++)
            {
                builder.matrix = Matrix4x4.Rotate(Quaternion.AngleAxis(360f * i / sailCount + Range(random, -2f, 2f), Vector3.forward));
                bool broken = i == brokenSail;
                float sailLength = broken ? length * 0.62f : length * Range(random, 0.96f, 1.02f);

                // Stock (spar), with a splintered end on the broken sail.
                builder.Box(new Vector3(0f, sailLength * 0.5f, 0f), new Vector3(sparWidth, sailLength, thickness * 1.6f), Quaternion.identity);
                if (broken)
                {
                    builder.Beam(new Vector3(0.05f, sailLength, 0f), new Vector3(0.45f, sailLength + 1.4f, -0.1f), 0.16f, 0.12f, Vector3.forward);
                    builder.Beam(new Vector3(-0.08f, sailLength - 0.1f, 0f), new Vector3(-0.2f, sailLength + 0.6f, 0f), 0.1f, 0.1f, Vector3.forward);
                }

                float x0 = sparWidth * 0.5f;
                float Width(float y) => frameWidth * Mathf.Lerp(1f, tipWidth, Mathf.InverseLerp(innerRadius, length, y));

                // Outer rail (hemlath) following the taper, in short pieces so it can be broken too.
                float railEnd = sailLength - 0.2f;
                for (float y = innerRadius; y < railEnd; y += 1.5f)
                {
                    float y1 = Mathf.Min(y + 1.5f, railEnd);
                    if (broken && y > sailLength * 0.7f) break;
                    builder.Beam(new Vector3(x0 + Width(y), y, 0f), new Vector3(x0 + Width(y1), y1, 0f), 0.15f, thickness, Vector3.forward);
                }

                // Sail bars across, unevenly spaced, slightly tilted, some missing (never two in a row).
                float[] barY = new float[bars + 1];
                bool previousMissing = false;
                for (int b = 0; b <= bars; b++)
                {
                    float y = Mathf.Lerp(innerRadius, length - 0.3f, (b + Range(random, -0.18f, 0.18f)) / bars);
                    barY[b] = y;
                    if (y > railEnd) continue;
                    bool missing = !previousMissing && b > 0 && random.NextDouble() < missingBars;
                    previousMissing = missing;
                    if (missing) continue;
                    float tilt = Range(random, -0.12f, 0.12f);
                    builder.Beam(new Vector3(x0, y, 0f), new Vector3(x0 + Width(y), y + tilt, 0f), Range(random, 0.2f, 0.32f), thickness, Vector3.forward);
                }

                // Torn canvas in some bays: attached at the spar, the free edge is a ragged sawtooth.
                for (int b = 0; b < bars; b++)
                {
                    float ya = barY[b], yb = barY[b + 1];
                    if (yb > railEnd || random.NextDouble() > canvas) continue;
                    float reach = Range(random, 0.45f, 1f);
                    int teeth = 5;
                    for (int t = 0; t < teeth; t++)
                    {
                        float y0 = Mathf.Lerp(ya, yb, t / (float)teeth), y1 = Mathf.Lerp(ya, yb, (t + 1f) / teeth);
                        float edge0 = x0 + Width(y0) * reach * (t % 2 == 0 ? 1f : Range(random, 0.55f, 0.8f));
                        float edge1 = x0 + Width(y1) * reach * (t % 2 == 1 ? 1f : Range(random, 0.55f, 0.8f));
                        DoubleQuad(builder, new Vector3(x0, y0, 0.08f), new Vector3(x0, y1, 0.08f), new Vector3(edge1, y1, 0.08f), new Vector3(edge0, y0, 0.08f));
                    }
                }

                // Rags: a few strips streaming off the outer rail.
                int rags = random.Next(1, 4);
                for (int r = 0; r < rags; r++)
                {
                    float y = Range(random, innerRadius + 1f, railEnd - 0.5f);
                    float x = x0 + Width(y);
                    float rag = Range(random, 0.9f, 2.4f);
                    Vector3 a = new Vector3(x, y, 0.08f);
                    Vector3 mid = a + new Vector3(rag * 0.55f, -rag * 0.25f, 0f);
                    Vector3 end = a + new Vector3(rag, -rag * 0.65f, 0f);
                    Vector3 w = new Vector3(0f, 0.11f, 0f);
                    DoubleQuad(builder, a - w, a + w, mid + w * 0.8f, mid - w * 0.8f);
                    DoubleQuad(builder, mid - w * 0.8f, mid + w * 0.8f, end + w * 0.2f, end - w * 0.2f);
                }

                // Leading board on the other side of the spar.
                builder.Box(new Vector3(-0.38f, (innerRadius + sailLength) * 0.5f, 0f), new Vector3(0.14f, sailLength - innerRadius, thickness), Quaternion.identity);
            }
            builder.matrix = Matrix4x4.identity;
        }

        static void DoubleQuad(NitrateMeshBuilder builder, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            builder.Quad(a, b, c, d);
            builder.Quad(d, c, b, a);
        }
    }
}
