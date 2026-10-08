using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Small set pieces: the log bridge with its posts, a leaning fence post, a cart wheel.
    /// </summary>
    public class NitrateProps : NitrateGenerator
    {
        public enum Kind { Bridge, FencePost, CartWheel }

        public Kind kind = Kind.Bridge;
        [Tooltip("Bridge: length along X. Fence post: height. Cart wheel: radius.")]
        public float size = 8f;

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            switch (kind)
            {
                case Kind.Bridge: Bridge(builder, random); break;
                case Kind.FencePost: FencePost(builder, random); break;
                case Kind.CartWheel: Wheel(builder); break;
            }
        }

        void Bridge(NitrateMeshBuilder builder, System.Random random)
        {
            // Three logs side by side; their tops are at y = 0, where the figure stands.
            for (int i = 0; i < 3; i++)
            {
                float r = Range(random, 0.17f, 0.22f);
                float z = (i - 1) * 0.36f;
                float half = size * 0.5f + Range(random, -0.4f, 0.4f);
                builder.Tube(new[] { new Vector3(-half, -r, z), new Vector3(0f, -r - 0.04f, z + Range(random, -0.05f, 0.05f)), new Vector3(half, -r, z) },
                             new[] { r, r * 0.95f, r * 0.85f }, 8);
            }
            // Posts at the ends, down into the water; the right back one leans and sticks up.
            for (int i = 0; i < 4; i++)
            {
                bool right = i >= 2;
                float x = (right ? 0.5f : -0.5f) * size * 0.92f;
                float z = i % 2 == 0 ? -0.6f : 0.6f;
                bool tall = right && z > 0f;
                Vector3 top = new Vector3(x, tall ? 0.9f : 0.15f, z);
                Vector3 bottom = new Vector3(x + (tall ? 0.35f : 0f), -2.5f, z);
                builder.Cylinder(bottom, top, 0.12f, 0.1f, 6);
            }
        }

        void FencePost(NitrateMeshBuilder builder, System.Random random)
        {
            Vector3 top = new Vector3(0.25f, size, 0f);
            builder.Tube(new[] { new Vector3(0f, -1f, 0f), new Vector3(0.1f, size * 0.5f, 0f), top }, new[] { 0.09f, 0.08f, 0.07f }, 6);
            builder.Cylinder(top, top + new Vector3(0.06f, 0.12f, 0f), 0.05f, 0f, 4); // split top
            builder.Beam(new Vector3(0.05f, size * 0.7f, 0f), new Vector3(-0.9f, size * 0.55f, 0.05f), 0.06f, 0.05f, Vector3.up); // broken rail
        }

        void Wheel(NitrateMeshBuilder builder)
        {
            float radius = size;
            builder.Ring(Vector3.zero, Vector3.forward, radius, 0.05f, 24, 5);
            builder.Ring(Vector3.zero, Vector3.forward, radius * 0.93f, 0.025f, 24, 4);
            builder.Cylinder(new Vector3(0f, 0f, -0.12f), new Vector3(0f, 0f, 0.12f), 0.12f, 0.12f, 8);
            for (int i = 0; i < 10; i++)
            {
                if (i == 6)
                    continue; // a missing spoke
                float a = 2f * Mathf.PI * i / 10f;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                builder.Cylinder(dir * 0.1f, dir * radius * 0.95f, 0.03f, 0.022f, 4);
            }
        }
    }
}
