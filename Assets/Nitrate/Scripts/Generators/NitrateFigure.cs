using System.Collections.Generic;
using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// The Lamplighter, in profile, facing +X, 1.75 m to the top of the hat. The camera never sees him from another
    /// side, so he is built like a cut-paper puppet: hand-placed outlines, smoothed and extruded, which is the only way
    /// to get a silhouette with character (a hunch, a nose, a stovepipe hat, a stride) out of a few hundred triangles.
    /// Body and Scarf are separate parts: the scarf is a child on its own pivot so it can flutter.
    /// </summary>
    public class NitrateFigure : NitrateGenerator
    {
        public enum Part { Body, Arms, Scarf }

        public Part part = Part.Body;
        [Tooltip("Extrusion depth of the body, metres.")]
        public float thickness = 0.16f;

        /// <summary>Where the lantern hangs (end of the pole's hook), in the figure's space.</summary>
        public static Vector3 PoleTip => new Vector3(0.95f, 1.83f, 0f);
        /// <summary>Pivot of the Arms part: the shoulder. The arms and pole turn around it.</summary>
        public static Vector3 ShoulderPivot => new Vector3(0.02f, 1.27f, 0f);
        /// <summary>The pole tip relative to the shoulder, for a lantern parented to the Arms part.</summary>
        public static Vector3 PoleTipFromShoulder => PoleTip - ShoulderPivot;
        /// <summary>The eye, on the camera side of the head.</summary>
        public static Vector3 Eye => new Vector3(0.075f, 1.505f, -0.07f);
        /// <summary>Where the scarf is tied (collar, back of the neck).</summary>
        public static Vector3 ScarfRoot => new Vector3(-0.1f, 1.38f, 0f);

        static List<Vector2> P(params float[] xy)
        {
            var list = new List<Vector2>(xy.Length / 2);
            for (int i = 0; i + 1 < xy.Length; i += 2) list.Add(new Vector2(xy[i], xy[i + 1]));
            return list;
        }

        protected override void Build(NitrateMeshBuilder builder, System.Random random)
        {
            if (part == Part.Scarf)
            {
                // A ribbon trailing behind in an S-wave, tied at the origin (the collar), thinning to a ragged end.
                var scarf = P(0f, 0.03f, -0.1f, -0.01f, -0.2f, -0.02f, -0.3f, -0.08f, -0.4f, -0.1f, -0.5f, -0.17f, -0.56f, -0.24f,
                              -0.6f, -0.3f, -0.52f, -0.27f, -0.47f, -0.22f, -0.38f, -0.16f, -0.29f, -0.14f, -0.19f, -0.07f, -0.09f, -0.06f, 0f, -0.03f);
                builder.Outline(NitrateMeshBuilder.Smooth(scarf, 1), -0.03f, 0.03f);
                return;
            }

            float zf = -thickness * 0.5f, zb = thickness * 0.5f;

            if (part == Part.Arms)
            {
                builder.matrix = Matrix4x4.Translate(-ShoulderPivot); // authored in figure space, pivots at the shoulder
                BuildArms(builder);
                builder.matrix = Matrix4x4.identity;
                return;
            }

            // Long coat: hunched back, A-line, torn hem with notches (back of the hem on the left).
            var coat = P(-0.31f, 0.40f, -0.23f, 0.34f, -0.18f, 0.41f, -0.11f, 0.33f, -0.05f, 0.40f, 0.03f, 0.36f, 0.11f, 0.42f, 0.2f, 0.44f,
                         0.18f, 0.62f, 0.155f, 0.85f, 0.14f, 1.05f, 0.12f, 1.22f, 0.09f, 1.33f,
                         0.03f, 1.39f, -0.06f, 1.41f, -0.15f, 1.35f,
                         -0.205f, 1.24f, -0.235f, 1.05f, -0.255f, 0.8f, -0.285f, 0.58f);
            builder.Outline(NitrateMeshBuilder.Smooth(coat, 2), zf, zb);

            // Turned-up collar hiding the neck.
            builder.Outline(NitrateMeshBuilder.Smooth(P(-0.14f, 1.32f, 0.08f, 1.34f, 0.085f, 1.44f, 0.0f, 1.47f, -0.13f, 1.46f), 1), zf * 1.1f, zb * 1.1f);

            // Head in profile: brow, long nose, chin.
            builder.Outline(NitrateMeshBuilder.Smooth(P(-0.07f, 1.45f, -0.05f, 1.53f, 0.02f, 1.565f, 0.075f, 1.54f, 0.09f, 1.505f,
                                                         0.16f, 1.465f, 0.1f, 1.45f, 0.085f, 1.41f, 0.03f, 1.395f, -0.03f, 1.41f), 1), zf * 0.8f, zb * 0.8f);

            // Stovepipe hat: a brim that bends down at the front, a crown tilted back with a dent.
            builder.Outline(P(-0.16f, 1.535f, 0.0f, 1.548f, 0.13f, 1.552f, 0.21f, 1.535f, 0.215f, 1.55f, 0.13f, 1.572f, 0.0f, 1.57f, -0.16f, 1.557f), zf * 1.4f, zb * 1.4f);
            builder.Outline(NitrateMeshBuilder.Smooth(P(-0.075f, 1.56f, 0.085f, 1.565f, 0.075f, 1.66f, 0.095f, 1.73f, 0.04f, 1.745f,
                                                         0.0f, 1.735f, -0.035f, 1.745f, -0.095f, 1.725f), 1), zf * 1.1f, zb * 1.1f);

            // Legs in mid-stride below the hem: front foot flat, back heel raised. Pointed boots.
            builder.Outline(P(0.0f, 0.43f, 0.09f, 0.43f, 0.105f, 0.24f, 0.095f, 0.075f, 0.19f, 0.06f, 0.255f, 0.0f, 0.02f, 0.0f, 0.025f, 0.08f, 0.035f, 0.24f), zf * 0.7f, zb * 0.7f);
            builder.Outline(P(-0.13f, 0.43f, -0.05f, 0.43f, -0.085f, 0.23f, -0.11f, 0.11f, -0.045f, 0.025f, -0.03f, 0.0f, -0.13f, 0.0f, -0.215f, 0.06f, -0.2f, 0.11f, -0.165f, 0.23f), zf * 0.5f, zb * 0.5f);

        }

        static void BuildArms(NitrateMeshBuilder builder)
        {
            // Arms: both hands on the pole, which is held low and angled up and forward.
            builder.Tube(new[] { new Vector3(0.05f, 1.27f, 0.04f), new Vector3(0.17f, 1.1f, 0.06f), new Vector3(0.29f, 1.16f, 0.06f) }, new[] { 0.042f, 0.036f, 0.03f }, 6);
            builder.Tube(new[] { new Vector3(-0.02f, 1.25f, -0.05f), new Vector3(0.06f, 1.02f, -0.06f), new Vector3(0.2f, 0.99f, -0.05f) }, new[] { 0.04f, 0.035f, 0.03f }, 6);
            builder.Sphere(new Vector3(0.29f, 1.16f, 0.0f), 0.04f, 4, 6);
            builder.Sphere(new Vector3(0.2f, 0.99f, 0.0f), 0.038f, 4, 6);

            // The crooked pole, a branch with a knot and a hook at the end.
            builder.Tube(new[] { new Vector3(0.1f, 0.74f, 0f), new Vector3(0.2f, 0.99f, 0f), new Vector3(0.29f, 1.16f, 0f), new Vector3(0.47f, 1.42f, 0f),
                                 new Vector3(0.6f, 1.6f, 0f), new Vector3(0.75f, 1.82f, 0f), new Vector3(0.88f, 1.93f, 0f), new Vector3(0.96f, 1.9f, 0f) },
                         new[] { 0.02f, 0.019f, 0.018f, 0.017f, 0.02f, 0.015f, 0.013f, 0.011f }, 5);
            builder.Tube(new[] { new Vector3(0.96f, 1.9f, 0f), new Vector3(0.975f, 1.86f, 0f), PoleTip }, new[] { 0.009f, 0.008f, 0.007f }, 4);
            builder.Cylinder(new Vector3(0.6f, 1.6f, 0f), new Vector3(0.66f, 1.63f, 0f), 0.014f, 0f, 4); // a broken twig on the knot
        }
    }
}
