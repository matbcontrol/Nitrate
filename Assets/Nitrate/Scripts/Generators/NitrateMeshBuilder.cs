using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nitrate
{
    /// <summary>
    /// Tiny mesh builder for silhouettes: tubes along polylines, boxes, rings. Faces wind clockwise seen from
    /// outside (Unity's front face), so back-face culling works and shadows are cast from the right side.
    /// Every vertex also carries its wind response in the vertex colour, read by the Nitrate/Silhouette shader:
    /// R = stiffness (1 - flexibility), G = phase.
    /// </summary>
    public class NitrateMeshBuilder
    {
        readonly List<Vector3> m_Vertices = new List<Vector3>(4096);
        readonly List<int> m_Triangles = new List<int>(8192);
        readonly List<Color> m_Colors = new List<Color>(4096);
        float m_FlexOverride = float.NaN;

        /// <summary>Applied to every vertex added after it is set.</summary>
        public Matrix4x4 matrix = Matrix4x4.identity;
        /// <summary>Wind flexibility of vertices added from now on: 0 rigid, 1 a free tip.</summary>
        public float flexibility;
        /// <summary>Flutter phase (0..1) of vertices added from now on. Give each blade or strand its own.</summary>
        public float phase;
        /// <summary>Optional: flexibility from the vertex position (mesh space), used when no explicit value is given.</summary>
        public System.Func<Vector3, float> flexibilityAt;

        public int TriangleCount => m_Triangles.Count / 3;

        int Vertex(Vector3 position)
        {
            Vector3 p = matrix.MultiplyPoint3x4(position);
            m_Vertices.Add(p);
            float flex = !float.IsNaN(m_FlexOverride) ? m_FlexOverride : flexibilityAt != null ? flexibilityAt(p) : flexibility;
            m_Colors.Add(new Color(1f - Mathf.Clamp01(flex), phase, 0f, 1f));
            return m_Vertices.Count - 1;
        }

        void Triangle(int a, int b, int c)
        {
            m_Triangles.Add(a);
            m_Triangles.Add(b);
            m_Triangles.Add(c);
        }

        /// <summary>
        /// A tube along a polyline with a radius per point. The ring frame is carried along the curve by parallel
        /// transport, so the tube does not twist. A radius of 0 at the end gives a spike.
        /// </summary>
        public void Tube(IReadOnlyList<Vector3> points, IReadOnlyList<float> radii, int sides, bool capStart = true, bool capEnd = true, bool closed = false,
                         IReadOnlyList<float> flex = null)
        {
            int count = points.Count;
            if (count < 2)
                return;
            sides = Mathf.Max(3, sides);
            var rings = new int[count];
            Vector3 normal = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                Vector3 previous = closed ? points[(i - 1 + count) % count] : points[Mathf.Max(i - 1, 0)];
                Vector3 next = closed ? points[(i + 1) % count] : points[Mathf.Min(i + 1, count - 1)];
                Vector3 tangent = (next - previous).normalized;
                if (i == 0)
                    normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
                else
                    normal = (normal - tangent * Vector3.Dot(normal, tangent)).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal);

                rings[i] = m_Vertices.Count;
                m_FlexOverride = flex != null ? flex[i] : float.NaN;
                for (int s = 0; s < sides; s++)
                {
                    float angle = 2f * Mathf.PI * s / sides;
                    Vertex(points[i] + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radii[i]);
                }
            }
            m_FlexOverride = float.NaN;

            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                int lower = rings[i];
                int upper = rings[(i + 1) % count];
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    Triangle(lower + s, lower + s1, upper + s1);
                    Triangle(lower + s, upper + s1, upper + s);
                }
            }

            if (closed)
                return;
            if (capStart && radii[0] > 0f)
            {
                m_FlexOverride = flex != null ? flex[0] : float.NaN;
                int center = Vertex(points[0]);
                m_FlexOverride = float.NaN;
                for (int s = 0; s < sides; s++)
                    Triangle(center, rings[0] + (s + 1) % sides, rings[0] + s);
            }
            if (capEnd && radii[count - 1] > 0f)
            {
                m_FlexOverride = flex != null ? flex[count - 1] : float.NaN;
                int center = Vertex(points[count - 1]);
                m_FlexOverride = float.NaN;
                int ring = rings[count - 1];
                for (int s = 0; s < sides; s++)
                    Triangle(center, ring + s, ring + (s + 1) % sides);
            }
        }

        public void Cylinder(Vector3 from, Vector3 to, float radiusFrom, float radiusTo, int sides)
        {
            Tube(new[] { from, to }, new[] { radiusFrom, radiusTo }, sides);
        }

        /// <summary>A sphere as a tube whose radius follows a half circle.</summary>
        public void Sphere(Vector3 center, float radius, int rings = 6, int sides = 8)
        {
            var points = new Vector3[rings + 1];
            var radii = new float[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                points[i] = center + Vector3.down * Mathf.Cos(a) * radius;
                radii[i] = Mathf.Sin(a) * radius;
            }
            Tube(points, radii, sides, false, false);
        }

        /// <summary>A ring (torus) in the plane perpendicular to axis.</summary>
        public void Ring(Vector3 center, Vector3 axis, float radius, float thickness, int segments = 16, int sides = 4)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            var points = new Vector3[segments];
            var radii = new float[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                points[i] = center + rotation * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                radii[i] = thickness;
            }
            Tube(points, radii, sides, false, false, true);
        }

        /// <summary>A box with flat faces (4 vertices per face).</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size * 0.5f;
            Vector3 x = rotation * new Vector3(h.x, 0f, 0f);
            Vector3 y = rotation * new Vector3(0f, h.y, 0f);
            Vector3 z = rotation * new Vector3(0f, 0f, h.z);
            // For each face: outward normal n and in-plane half axes u, v with cross(u, v) pointing along n.
            Face(center + x, y, z);
            Face(center - x, z, y);
            Face(center + y, z, x);
            Face(center - y, x, z);
            Face(center + z, x, y);
            Face(center - z, y, x);
        }

        /// <summary>A box spanning from one point to another, with the given cross-section.</summary>
        public void Beam(Vector3 from, Vector3 to, float width, float depth, Vector3 up)
        {
            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 1e-8f)
                return;
            Quaternion rotation = Quaternion.LookRotation(dir, up);
            Box((from + to) * 0.5f, new Vector3(width, depth, dir.magnitude), rotation);
        }

        /// <summary>
        /// A flat shape extruded in depth: the outline is drawn in the XY plane (x right, y up, any winding), cut into
        /// triangles by ear clipping, capped front (facing -z, the camera) and back, and closed with side walls.
        /// This is how the side-on characters are built: the outline IS the character.
        /// </summary>
        public void Outline(IList<Vector2> outline, float zFront, float zBack)
        {
            var points = new List<Vector2>(outline);
            if (SignedArea(points) < 0f)
                points.Reverse(); // counter-clockwise from here on
            int n = points.Count;
            if (n < 3)
                return;

            var front = new int[n];
            var back = new int[n];
            for (int i = 0; i < n; i++)
            {
                front[i] = Vertex(new Vector3(points[i].x, points[i].y, zFront));
                back[i] = Vertex(new Vector3(points[i].x, points[i].y, zBack));
            }
            foreach (var (a, b, c) in EarClip(points))
            {
                Triangle(front[a], front[c], front[b]); // faces -z
                Triangle(back[a], back[b], back[c]);    // faces +z
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 p0 = new Vector3(points[i].x, points[i].y, zFront), p1 = new Vector3(points[j].x, points[j].y, zFront);
                Quad(p0, p1, new Vector3(p1.x, p1.y, zBack), new Vector3(p0.x, p0.y, zBack));
            }
        }

        /// <summary>Chaikin corner cutting on a closed outline: each pass rounds every corner a little.</summary>
        public static List<Vector2> Smooth(IList<Vector2> outline, int passes)
        {
            var points = new List<Vector2>(outline);
            for (int pass = 0; pass < passes; pass++)
            {
                var next = new List<Vector2>(points.Count * 2);
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 a = points[i], b = points[(i + 1) % points.Count];
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                    next.Add(Vector2.Lerp(a, b, 0.75f));
                }
                points = next;
            }
            return points;
        }

        static float SignedArea(IList<Vector2> p)
        {
            float area = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        // Ear clipping for a simple counter-clockwise polygon: repeatedly cut off a convex corner whose triangle
        // contains no other vertex. O(n^2), fine for outlines of a few hundred points.
        static List<(int, int, int)> EarClip(List<Vector2> p)
        {
            var result = new List<(int, int, int)>();
            var remaining = new List<int>();
            for (int i = 0; i < p.Count; i++) remaining.Add(i);
            int guard = 0;
            while (remaining.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int k = 0; k < remaining.Count; k++)
                {
                    int ia = remaining[(k - 1 + remaining.Count) % remaining.Count], ib = remaining[k], ic = remaining[(k + 1) % remaining.Count];
                    Vector2 a = p[ia], b = p[ib], c = p[ic];
                    if (Cross(b - a, c - b) <= 1e-9f)
                        continue; // reflex corner
                    bool inside = false;
                    foreach (int m in remaining)
                    {
                        if (m == ia || m == ib || m == ic) continue;
                        if (InTriangle(p[m], a, b, c)) { inside = true; break; }
                    }
                    if (inside)
                        continue;
                    result.Add((ia, ib, ic));
                    remaining.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped)
                    break; // degenerate outline: give up on the rest rather than loop forever
            }
            if (remaining.Count == 3)
                result.Add((remaining[0], remaining[1], remaining[2]));
            return result;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            return Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;
        }

        /// <summary>A quad whose corners go clockwise as seen from the side that should be visible.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int ia = Vertex(a), ib = Vertex(b), ic = Vertex(c), id = Vertex(d);
            Triangle(ia, ib, ic);
            Triangle(ia, ic, id);
        }

        /// <summary>A quad with its lower edge (a, d) at one flexibility and its upper edge (b, c) at another: a blade segment.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float lowerFlex, float upperFlex)
        {
            m_FlexOverride = lowerFlex;
            int ia = Vertex(a), id = Vertex(d);
            m_FlexOverride = upperFlex;
            int ib = Vertex(b), ic = Vertex(c);
            m_FlexOverride = float.NaN;
            Triangle(ia, ib, ic);
            Triangle(ia, ic, id);
        }

        void Face(Vector3 center, Vector3 u, Vector3 v)
        {
            int a = Vertex(center - u - v);
            int b = Vertex(center + u - v);
            int c = Vertex(center + u + v);
            int d = Vertex(center - u + v);
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        public void Fill(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = m_Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(m_Vertices);
            mesh.SetColors(m_Colors);
            mesh.SetTriangles(m_Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
