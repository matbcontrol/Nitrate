using System.Collections.Generic;
using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Smock windmill body: an eight-sided weatherboarded tower leaning away from the wind, a gallery with a broken
    /// railing, a half-stripped cap, the hub axle and a fantail. Everything is built for the outline: the camera sees
    /// the mill against the moon, so the cap is where the damage goes (missing boards, bare ribs, a bent finial), and
    /// a few boards missing front and back let the moon shine through the tower.
    /// The sails are a separate object (NitrateSails + NitrateRotor) placed at HubPosition, so they can turn.
    /// </summary>
    public class NitrateMill : NitrateGenerator
    {
        public float towerHeight = 13f;
        public float baseRadius = 3f;
        public float topRadius = 1.9f;
        public float capHeight = 3.2f;
        public float galleryHeight = 4.5f;
        public float galleryWidth = 1.4f;
        [Tooltip("How far the hub sticks out in front of the cap (local -z faces the camera).")]
        public float hubOffset = 2.4f;
        [Tooltip("Lean of the whole body away from the wind (toward -x), degrees, around the foot of the tower.")]
        public float lean = 2.5f;
        [Tooltip("Height of one weatherboard band. Each band overlaps the one below, so the outline is finely stepped.")]
        public float boardHeight = 0.65f;

        const int k_TowerSides = 8;
        const int k_CapSegments = 12;

        Quaternion Lean => Quaternion.Euler(0f, 0f, lean);

        public Vector3 HubPosition => Lean * new Vector3(0f, towerHeight + capHeight * 0.45f, -hubOffset);

        float TowerRadius(float y)
        {
            float t = Mathf.Clamp01(y / towerHeight);
            return Mathf.Lerp(baseRadius, topRadius, t) * (1f + 0.04f * Mathf.Sin(t * Mathf.PI));
        }

        // Point on an eight-sided ring. Corner k sits at angle (k + 0.5) / 8 turns, so face 5 (between corners 5 and 6)
        // looks straight at the camera (-z) and face 1 straight at the moon (+z).
        static Vector3 Corner(int k, float radius, float y)
        {
            float a = 2f * Mathf.PI * (k + 0.5f) / k_TowerSides;
            return new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
        }

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            builder.matrix = Matrix4x4.Rotate(Lean);
            BuildTower(builder);
            BuildGallery(builder);
            BuildCap(builder);

            // Axle from the cap to the hub, and the hub itself.
            Vector3 hub = new Vector3(0f, towerHeight + capHeight * 0.45f, -hubOffset);
            builder.Cylinder(new Vector3(0f, hub.y, 0.3f), hub, 0.28f, 0.28f, 8);
            builder.Cylinder(hub + Vector3.forward * 0.35f, hub - Vector3.forward * 0.45f, 0.65f, 0.55f, 10);

            // Fantail at the back of the cap.
            Vector3 tail = new Vector3(0f, towerHeight + 0.6f, topRadius + 2.6f);
            builder.Beam(new Vector3(0f, towerHeight + 0.6f, topRadius * 0.8f), tail, 0.12f, 0.12f, Vector3.up);
            for (int i = 0; i < 6; i++)
            {
                float a = 2f * Mathf.PI * i / 6f;
                builder.Beam(tail, tail + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.9f, 0.05f, 0.18f, Vector3.forward);
            }

            // Hung in world space (plumb), so built without the lean.
            Matrix4x4 leaned = builder.matrix;
            builder.matrix = Matrix4x4.identity;
            BuildRope(builder, leaned);
            BuildLadder(builder, leaned);
        }

        void BuildTower(NitrateMeshBuilder builder)
        {
            // Bands where a board is gone front and back: moonlight passes straight through. Each hole gets a brace.
            var holes = new HashSet<int>();
            int bands = Mathf.Max(4, Mathf.RoundToInt(towerHeight / boardHeight));
            float band = towerHeight / bands;
            holes.Add(Mathf.RoundToInt(bands * 0.62f));
            holes.Add(Mathf.RoundToInt(bands * 0.62f) + 1);
            holes.Add(Mathf.RoundToInt(bands * 0.78f));

            for (int b = 0; b < bands; b++)
            {
                float y0 = b * band, y1 = (b + 1) * band;
                // The bottom edge of each board stands out a little: the overlap gives the outline its fine steps.
                float r0 = TowerRadius(y0) + 0.07f, r1 = TowerRadius(y1);
                for (int k = 0; k < k_TowerSides; k++)
                {
                    bool missing = holes.Contains(b) && (k == 5 || k == 1);
                    if (missing)
                        continue;
                    int n = (k + 1) % k_TowerSides;
                    builder.Quad(Corner(k, r0, y0), Corner(k, r1, y1), Corner(n, r1, y1), Corner(n, r0, y0));
                    // The step itself: a thin ledge under the board, facing down and out.
                    if (b > 0)
                    {
                        float inner = TowerRadius(y0);
                        builder.Quad(Corner(k, inner, y0), Corner(k, r0, y0), Corner(n, r0, y0), Corner(n, inner, y0));
                    }
                }
                if (holes.Contains(b))
                {
                    // A diagonal brace across each opening, front and back, so the light comes through in slivers.
                    float rm = TowerRadius((y0 + y1) * 0.5f);
                    builder.Cylinder(Corner(5, rm, y0), Corner(6, rm, y1), 0.06f, 0.06f, 4);
                    builder.Cylinder(Corner(1, rm, y1), Corner(2, rm, y0), 0.06f, 0.06f, 4);
                }
            }
            // Corner posts of the smock frame, visible inside the openings.
            for (int k = 0; k < k_TowerSides; k++)
                builder.Cylinder(Corner(k, TowerRadius(0f) - 0.05f, 0f), Corner(k, TowerRadius(towerHeight) - 0.05f, towerHeight), 0.1f, 0.08f, 4);
        }

        void BuildGallery(NitrateMeshBuilder builder)
        {
            // A deck ring with railing posts. A run of posts is missing and one hangs down, broken.
            float deckRadius = TowerRadius(galleryHeight) + galleryWidth;
            builder.Ring(new Vector3(0f, galleryHeight, 0f), Vector3.up, deckRadius - galleryWidth * 0.5f, 0.12f, 24, 4);
            builder.Ring(new Vector3(0f, galleryHeight + 1.05f, 0f), Vector3.up, deckRadius, 0.04f, 32, 3);
            int posts = 28;
            for (int i = 0; i < posts; i++)
            {
                if (i >= 9 && i <= 12)
                    continue;
                float a = 2f * Mathf.PI * i / posts;
                Vector3 bottom = new Vector3(Mathf.Cos(a) * deckRadius, galleryHeight, Mathf.Sin(a) * deckRadius);
                Vector3 top = bottom + Vector3.up * 1.05f;
                if (i == 13)
                    top = bottom + new Vector3(0.4f, -0.9f, 0.3f); // the broken one hangs over the edge
                builder.Cylinder(bottom, top, 0.045f, 0.04f, 4);
            }
            // Struts under the deck.
            for (int i = 0; i < 8; i++)
            {
                float a = 2f * Mathf.PI * (i + 0.5f) / 8f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float r = TowerRadius(galleryHeight - 2f);
                builder.Cylinder(dir * r + Vector3.up * (galleryHeight - 2f), dir * deckRadius + Vector3.up * galleryHeight, 0.08f, 0.06f, 4);
            }
        }

        void BuildCap(NitrateMeshBuilder builder)
        {
            // An ogee dome tilted back, boarded in twelve segments. The hub hides its middle, so the damage goes on its
            // left and right edges, where the outline is cut against the moon: boards gone, bare ribs, one loose board
            // hanging off the rim, a bent finial.
            float[] profile = { 1.08f, 1.14f, 0.98f, 0.66f, 0.3f, 0.06f };
            int rings = profile.Length;
            var ring = new Vector3[rings, k_CapSegments + 1];
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                float y = towerHeight + capHeight * t;
                float z = 0.35f * t;
                for (int j = 0; j <= k_CapSegments; j++)
                {
                    float a = 2f * Mathf.PI * j / k_CapSegments;
                    ring[i, j] = new Vector3(Mathf.Cos(a) * topRadius * profile[i], y, Mathf.Sin(a) * topRadius * profile[i] + z);
                }
            }

            // Segment j spans j/12 to (j+1)/12 of a turn: 5 and 6 meet at the left edge of the outline, 11 and 0 at the right.
            var missing = new HashSet<(int, int)> { (2, 5), (2, 6), (3, 5), (3, 6), (3, 7), (4, 6), (3, 11), (3, 0), (4, 0) };
            int[] ribs = { 5, 6, 7, 11, 12 };
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < k_CapSegments; j++)
                    if (!missing.Contains((i, j)))
                        builder.Quad(ring[i, j], ring[i + 1, j], ring[i + 1, j + 1], ring[i, j + 1]);
            // Close the base of the dome (it sits on the tower).
            for (int j = 0; j < k_CapSegments; j++)
                builder.Quad(ring[0, j + 1], new Vector3(0f, towerHeight, 0f), new Vector3(0f, towerHeight, 0f), ring[0, j]);

            // Bare ribs over the opening, a little proud of the surface.
            foreach (int j in ribs)
            {
                var points = new Vector3[rings];
                var radii = new float[rings];
                for (int i = 0; i < rings; i++)
                {
                    Vector3 c = new Vector3(0f, ring[i, j].y, 0.35f * i / (rings - 1));
                    points[i] = c + (ring[i, j] - c) * 1.03f;
                    radii[i] = 0.07f;
                }
                builder.Tube(points, radii, 4);
            }

            // A loose board hanging from the rim on the left, and the bent finial on top.
            Vector3 rim = ring[1, 6];
            float rimY = builder.matrix.MultiplyPoint3x4(rim).y;
            builder.flexibilityAt = p => Mathf.Clamp01((rimY - p.y) / 1.3f) * 0.8f;
            builder.Beam(rim, rim + new Vector3(-0.35f, -1.3f, -0.1f), 0.22f, 0.04f, Vector3.forward);
            builder.flexibilityAt = null;
            Vector3 top = new Vector3(0f, towerHeight + capHeight, 0.35f);
            builder.Tube(new[] { top, top + new Vector3(0.05f, 0.7f, 0f), top + new Vector3(0.35f, 1.05f, 0f) }, new[] { 0.08f, 0.05f, 0.03f }, 4);
            builder.Sphere(top + new Vector3(0.05f, 0.45f, 0f), 0.13f, 3, 6);
        }

        // A rope from the gallery rail, plumb, its free end swinging in the wind.
        void BuildRope(NitrateMeshBuilder builder, Matrix4x4 leaned)
        {
            float deckRadius = TowerRadius(galleryHeight) + galleryWidth;
            float a = 2f * Mathf.PI * 13.5f / 28f; // the gap in the railing, left side
            Vector3 anchor = leaned.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * deckRadius, galleryHeight + 1.0f, Mathf.Sin(a) * deckRadius));
            const float length = 3.6f;
            const int segments = 8;
            var points = new Vector3[segments + 1];
            var radii = new float[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                points[i] = anchor + new Vector3(0.25f * t * t, -length * t, 0f);
                radii[i] = 0.045f;
            }
            float topY = anchor.y;
            builder.flexibilityAt = p => Mathf.Clamp01((topY - p.y) / length) * 1.4f;
            builder.phase = 0.3f;
            builder.Tube(points, radii, 4);
            builder.flexibilityAt = null;
            builder.phase = 0f;
        }

        // A ladder propped on the gallery on the right, two rungs missing. Its face is turned to the camera:
        // seen edge-on, a ladder is just one more line.
        void BuildLadder(NitrateMeshBuilder builder, Matrix4x4 leaned)
        {
            float deckRadius = TowerRadius(galleryHeight) + galleryWidth;
            Vector3 topCenter = leaned.MultiplyPoint3x4(new Vector3(deckRadius - 0.1f, galleryHeight + 0.6f, -0.6f));
            Vector3 footCenter = new Vector3(topCenter.x + 2.2f, 0.6f, topCenter.z);
            Vector3 along = (topCenter - footCenter).normalized;
            Vector3 side = new Vector3(-along.y, along.x, 0f) * 0.28f;
            builder.Cylinder(footCenter - side, topCenter - side, 0.05f, 0.045f, 4);
            builder.Cylinder(footCenter + side, topCenter + side, 0.05f, 0.045f, 4);
            int rungs = Mathf.FloorToInt(Vector3.Distance(footCenter, topCenter) / 0.35f);
            for (int i = 1; i < rungs; i++)
            {
                if (i == 5 || i == 6)
                    continue;
                Vector3 c = Vector3.Lerp(footCenter, topCenter, i / (float)rungs);
                builder.Cylinder(c - side, c + side, 0.03f, 0.03f, 4);
            }
        }
    }
}
