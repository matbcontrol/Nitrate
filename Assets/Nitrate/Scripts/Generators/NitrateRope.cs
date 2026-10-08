using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// A rope that sags between two local points (a parabola, close enough to a catenary for a slack rope),
    /// or, in Chain mode, a hanging chain of alternating links from the origin straight down.
    /// </summary>
    public class NitrateRope : NitrateGenerator
    {
        public enum Kind { Rope, Chain }

        public Kind kind = Kind.Rope;
        public Vector3 end = new Vector3(4f, -0.3f, 0f);
        [Tooltip("Rope: how far the middle hangs below the straight line, metres.")]
        public float sag = 0.6f;
        public float radius = 0.025f;
        [Tooltip("Chain: number of links.")]
        public int links = 14;
        public float linkLength = 0.14f;

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            if (kind == Kind.Chain)
            {
                for (int i = 0; i < links; i++)
                {
                    Vector3 center = Vector3.down * (i * linkLength * 0.8f + linkLength * 0.5f);
                    Vector3 axis = i % 2 == 0 ? Vector3.forward : Vector3.right;
                    builder.matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(1f, 1.6f, 1f));
                    builder.Ring(Vector3.zero, axis, linkLength * 0.3f, radius * 0.5f, 10, 4);
                }
                builder.matrix = Matrix4x4.identity;
                return;
            }

            const int segments = 16;
            var points = new Vector3[segments + 1];
            var radii = new float[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                points[i] = Vector3.Lerp(Vector3.zero, end, t) + Vector3.down * (4f * sag * t * (1f - t));
                radii[i] = radius;
            }
            builder.Tube(points, radii, 4);
        }
    }
}
