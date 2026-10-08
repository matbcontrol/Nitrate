using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Base for the procedural silhouette kit. The mesh is rebuilt from the parameters and the seed whenever they
    /// change, and is never saved into the scene (it is regenerated on load), so the scene file stays small
    /// and every shape can be tuned live in the Inspector.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public abstract class NitrateGenerator : MonoBehaviour
    {
        public int seed = 1;

        Mesh m_Mesh;
        bool m_Dirty = true;

        /// <summary>Triangles in the last generated mesh (shown in the Inspector tooltip of the HUD).</summary>
        public int TriangleCount { get; private set; }

        protected abstract void Build(NitrateMeshBuilder builder, System.Random random);

        protected static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
        protected static float Range(System.Random random, Vector2 range) => Range(random, range.x, range.y);

        protected static Vector3 RandomDirection(System.Random random)
        {
            Vector3 v;
            do
                v = new Vector3(Range(random, -1f, 1f), Range(random, -1f, 1f), Range(random, -1f, 1f));
            while (v.sqrMagnitude > 1f || v.sqrMagnitude < 1e-4f);
            return v.normalized;
        }

        void OnEnable() => Rebuild();

        // Meshes cannot be assigned inside OnValidate, so mark dirty and rebuild on the next update.
        void OnValidate() => m_Dirty = true;

        void Update()
        {
            if (m_Dirty)
                Rebuild();
        }

        public void Rebuild()
        {
            if (m_Mesh == null)
                m_Mesh = new Mesh { name = GetType().Name, hideFlags = HideFlags.DontSave };

            var builder = new NitrateMeshBuilder();
            Build(builder, new System.Random(seed));
            builder.Fill(m_Mesh);
            TriangleCount = builder.TriangleCount;
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Dirty = false;
        }

        void OnDestroy()
        {
            if (m_Mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(m_Mesh);
            else
                DestroyImmediate(m_Mesh);
        }
    }
}
