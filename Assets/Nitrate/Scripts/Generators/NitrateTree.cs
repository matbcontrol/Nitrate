using System.Collections.Generic;
using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Dead tree: a tall trunk with a root flare and roots along the ground, bending in smooth S-curves, splitting into
    /// fewer, longer branches than a living tree, ending in thorns, with broken stubs and strands of moss hanging down.
    /// Recursive: every branch spawns children along its upper part, each thinner, shorter and bent by gravity.
    /// </summary>
    public class NitrateTree : NitrateGenerator
    {
        public float height = 12f;
        public float baseRadius = 0.22f;
        [Range(1, 5)] public int levels = 4;
        public Vector2Int childrenPerBranch = new Vector2Int(2, 4);
        [Tooltip("Angle between a child branch and its parent, degrees.")]
        public Vector2 spread = new Vector2(25f, 55f);
        public float lengthRatio = 0.62f;
        public float radiusRatio = 0.5f;
        [Tooltip("How far the trunk leans, degrees.")]
        public float lean = 5f;
        [Tooltip("How much the branches wander. The bend is smoothed, so they curve rather than zigzag.")]
        public float gnarl = 0.14f;
        [Tooltip("Side branches sag under their weight.")]
        public float droop = 0.12f;
        [Tooltip("Short spikes on the outer branches.")]
        public int thorns = 40;
        [Range(0, 8)] public int roots = 4;
        [Range(0, 8)] public int stubs = 4;
        [Tooltip("Share of outer branch points with a hanging strand of moss or vine.")]
        [Range(0f, 1f)] public float strands = 0.3f;
        public Vector2 strandLength = new Vector2(0.6f, 2.8f);
        [Tooltip("Thinnest twig radius, metres. Keep it at 2 or more pixels at the hero distance.")]
        public float minRadius = 0.006f;
        public int sides = 6;
        [Tooltip("How much the branch tips move in the wind (0..1). The trunk base never moves; flexibility grows with "
               + "the distance from the base. Lower it for trees close to the camera, where a small sway looks big.")]
        [Range(0f, 1f)] public float windFlex = 1f;

        readonly List<(Vector3 position, Vector3 direction, float radius, int level)> m_Spots = new List<(Vector3, Vector3, float, int)>();

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            m_Spots.Clear();
            float reach = Mathf.Max(height, 0.01f);
            builder.flexibilityAt = p => Mathf.Pow(Mathf.Clamp01(p.magnitude / reach), 1.6f) * windFlex;
            Quaternion tilt = Quaternion.Euler(Range(random, -lean, lean), Range(random, 0f, 360f), Range(random, -lean, lean));
            List<Vector3> trunk = Branch(builder, random, Vector3.zero, tilt * Vector3.up, height, baseRadius, 0);

            // Roots: curling out along the ground, mostly sideways so they read in profile, then into the earth.
            for (int i = 0; i < roots; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                Vector3 dir = new Vector3(side, -0.15f, Range(random, -0.4f, 0.4f)).normalized;
                float length = Range(random, 0.6f, 1.6f) * (baseRadius * 6f);
                var points = new Vector3[5];
                var radii = new float[5];
                for (int s = 0; s < 5; s++)
                {
                    float t = s / 4f;
                    points[s] = new Vector3(0f, baseRadius * 1.5f * (1f - t), 0f) + dir * length * t + Vector3.down * (0.35f * length * t * t);
                    radii[s] = Mathf.Lerp(baseRadius * 0.55f, minRadius, t);
                }
                builder.Tube(points, radii, 5, false, false);
            }

            // Broken stubs on the trunk: short, thick, cut flat.
            for (int i = 0; i < stubs && trunk.Count > 3; i++)
            {
                int k = random.Next(2, trunk.Count - 2);
                Vector3 at = trunk[k];
                Vector3 axis = (trunk[k + 1] - trunk[k]).normalized;
                Vector3 out1 = Quaternion.AngleAxis(Range(random, 0f, 360f), axis) * Vector3.Cross(axis, Vector3.forward).normalized;
                Vector3 dir = (out1 + axis * 0.4f).normalized;
                float r = baseRadius * Range(random, 0.25f, 0.45f) * (1f - k / (float)trunk.Count);
                builder.Cylinder(at, at + dir * Range(random, 0.25f, 0.7f), r, r * 0.8f, 5);
            }

            // Thorns on the outer branches.
            var thornSpots = m_Spots.FindAll(s => s.level >= levels - 2);
            for (int i = 0; i < thorns && thornSpots.Count > 0; i++)
            {
                var spot = thornSpots[random.Next(thornSpots.Count)];
                Vector3 direction = (spot.direction + RandomDirection(random) * 1.2f).normalized;
                builder.Cylinder(spot.position, spot.position + direction * Range(random, 0.15f, 0.6f), Mathf.Max(spot.radius * 0.8f, minRadius), 0f, 3);
            }

            // Hanging strands: moss and dead vines straight down from branches, slightly wavy, tapering to nothing.
            // Strands hang in world space, even on a branch that lies on its side.
            Vector3 down = transform.InverseTransformDirection(Vector3.down);
            Vector3 across = Vector3.Cross(down, Vector3.forward).normalized;
            var strandSpots = m_Spots.FindAll(s => s.level >= 2);
            foreach (var spot in strandSpots)
            {
                if (random.NextDouble() > strands) continue;
                float length = Range(random, strandLength) * Mathf.Clamp01(height / 12f + 0.3f);
                var points = new Vector3[6];
                var radii = new float[6];
                var flex = new float[6];
                float rootFlex = Mathf.Pow(Mathf.Clamp01(spot.position.magnitude / reach), 1.6f) * windFlex;
                float sway = Range(random, -0.15f, 0.15f);
                for (int s = 0; s < 6; s++)
                {
                    float t = s / 5f;
                    points[s] = spot.position + across * (Mathf.Sin(t * 3f + sway * 10f) * 0.08f + sway * t) + down * (length * t);
                    radii[s] = Mathf.Lerp(Mathf.Max(spot.radius * 0.6f, 0.012f), 0f, t);
                    flex[s] = Mathf.Min(1f, rootFlex + 0.8f * t); // hanging moss swings much more than its branch
                }
                builder.phase = (float)random.NextDouble();
                builder.Tube(points, radii, 3, false, false, false, flex);
            }
        }

        List<Vector3> Branch(NitrateMeshBuilder builder, System.Random random, Vector3 start, Vector3 direction, float length, float radius, int level)
        {
            int segments = level == 0 ? 14 : 7;
            var points = new List<Vector3>(segments + 1) { start };
            var radii = new List<float>(segments + 1) { radius * (level == 0 ? 2.1f : 1f) };
            bool outermost = level == levels - 1;
            float tipRadius = outermost ? minRadius : radius * 0.35f;
            Vector3 position = start;
            Vector3 dir = direction;
            Vector3 bend = Vector3.zero;
            float segmentLength = length / segments;

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float sag = level == 0 ? 0f : droop * level / Mathf.Max(1, levels - 1);
                bend = bend * 0.85f + RandomDirection(random) * gnarl * 0.5f;
                dir = (dir + bend + Vector3.down * sag).normalized;
                position += dir * segmentLength;
                points.Add(position);
                float r = Mathf.Lerp(radius, tipRadius, t);
                if (level == 0) r *= 1f + 1.1f * Mathf.Exp(-14f * t); // root flare
                radii.Add(Mathf.Max(r, minRadius));
            }
            builder.Tube(points, radii, Mathf.Max(3, sides - level), level == 0, false);

            for (int i = 1; i < points.Count; i++)
                m_Spots.Add((points[i], (points[i] - points[i - 1]).normalized, radii[i], level));
            if (outermost)
                return points;

            int children = random.Next(childrenPerBranch.x, childrenPerBranch.y + 1);
            for (int c = 0; c < children; c++)
            {
                float along = Range(random, 0.35f, 0.97f);
                float exact = along * segments;
                int index = Mathf.Min(Mathf.FloorToInt(exact), segments - 1);
                Vector3 origin = Vector3.Lerp(points[index], points[index + 1], exact - index);
                float originRadius = Mathf.Lerp(radii[index], radii[index + 1], exact - index);

                Vector3 parentDir = (points[index + 1] - points[index]).normalized;
                Vector3 side = Vector3.Cross(parentDir, RandomDirection(random)).normalized;
                Vector3 childDir = Quaternion.AngleAxis(Range(random, spread), side) * parentDir;
                childDir = (childDir + Vector3.up * 0.25f).normalized; // dead trees reach up before they droop
                float childLength = length * lengthRatio * Range(random, 0.7f, 1.1f) * (1f - along * 0.35f);
                Branch(builder, random, origin, childDir, childLength, originRadius * radiusRatio * 1.6f, level + 1);
            }
            return points;
        }
    }
}
