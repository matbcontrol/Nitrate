using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// A small cage lantern hanging below this object's origin (the pivot is the hook, so it swings naturally).
    /// The glowing bulb is a separate child with an HDR unlit material; it makes the bloom and is the only
    /// coloured pixel in the frame.
    /// </summary>
    public class NitrateLantern : NitrateGenerator
    {
        public float hangLength = 0.12f;
        public float radius = 0.055f;
        public float height = 0.15f;

        public Vector3 BulbPosition => new Vector3(0f, -hangLength - 0.045f - height * 0.5f, 0f);

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            float top = -hangLength;
            builder.Cylinder(Vector3.zero, new Vector3(0f, top, 0f), 0.006f, 0.006f, 3);           // wire
            builder.Ring(new Vector3(0f, top + 0.01f, 0f), Vector3.right, 0.018f, 0.005f, 8, 3);   // handle
            builder.Cylinder(new Vector3(0f, top - 0.045f, 0f), new Vector3(0f, top, 0f), radius * 1.15f, 0.015f, 6); // roof
            float bottom = top - 0.045f - height;
            for (int i = 0; i < 4; i++)
            {
                float a = 2f * Mathf.PI * (i + 0.5f) / 4f;
                Vector3 offset = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                builder.Cylinder(new Vector3(0f, bottom, 0f) + offset, new Vector3(0f, top - 0.045f, 0f) + offset, 0.006f, 0.006f, 3);
            }
            builder.Cylinder(new Vector3(0f, bottom - 0.015f, 0f), new Vector3(0f, bottom, 0f), radius * 1.1f, radius * 1.1f, 8); // base
        }
    }
}
