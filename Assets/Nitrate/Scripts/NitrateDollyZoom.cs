using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nitrate
{
    /// <summary>
    /// Dolly zoom ("Vertigo effect"). The camera moves along its view axis while the field of view changes so that
    /// the plane through the pivot always shows the same height in metres: the figure keeps its size and place,
    /// while everything in front of and behind it changes perspective. t = 0 is telephoto (reads as flat 2D),
    /// t = 1 is wide (real depth). Plays a seamless loop, or follows the mouse wheel.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class NitrateDollyZoom : MonoBehaviour
    {
        [Tooltip("The point where the view axis crosses the figure's plane. The axis continues to the hub and the moon.")]
        public Transform pivot;
        [Tooltip("Metres of the figure's plane visible from the bottom to the top of the frame. Constant during the zoom.")]
        public float frameHeight = 5.5f;
        [Tooltip("Camera pitch in degrees, positive looks up.")]
        public float pitch = 2.5f;
        public float yaw;
        [Tooltip("Moves the optical axis off the frame centre (fraction of the frame). The point the camera aims at, "
               + "here the mill's hub in front of the moon, stays on that spot of the frame through the whole zoom.")]
        public Vector2 lensShift = new Vector2(-0.1667f, -0.2f);
        [Tooltip("Vertical field of view at t = 0 and t = 1.")]
        public Vector2 fovRange = new Vector2(15f, 45f);
        [Range(0f, 1f)] public float t;

        [Tooltip("Play the loop. The mouse wheel takes over until Space is pressed.")]
        public bool autoPlay = true;
        [Tooltip("Zoom over one loop (x: 0..1 of the loop, y: t).")]
        public AnimationCurve loop = DefaultLoop();
        public float wheelStep = 0.05f;

        Camera m_Camera;

        public float Distance => frameHeight / (2f * Mathf.Tan(0.5f * Mathf.Deg2Rad * Fov));
        public float Fov => Mathf.Lerp(fovRange.x, fovRange.y, t);

        // Hold flat for 3 s, open up over 5 s, hold wide for 2 s, close over 5 s back to the first frame.
        public static AnimationCurve DefaultLoop()
        {
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.2f, 0f), new Keyframe(0.5333f, 1f),
                new Keyframe(0.6667f, 1f), new Keyframe(1f, 0f));
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f); // flat tangents: ease in and out of every key
            return curve;
        }

        void OnEnable()
        {
            m_Camera = GetComponent<Camera>();
            Apply();
        }

        void LateUpdate()
        {
            if (Application.isPlaying)
            {
                HandleInput();
                if (autoPlay)
                    t = Mathf.Clamp01(loop.Evaluate(NitrateClock.LoopPhase));
            }
            Apply();
        }

        void HandleInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    autoPlay = false;
                    t = Mathf.Clamp01(t + Mathf.Sign(scroll) * wheelStep);
                }
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                autoPlay = !autoPlay;
                if (autoPlay)
                    NitrateClock.Restart();
            }
        }

        public void Apply()
        {
            if (pivot == null || m_Camera == null)
                return;

            Quaternion rotation = Quaternion.Euler(-pitch, yaw, 0f);
            float distance = Distance;
            transform.SetPositionAndRotation(pivot.position - rotation * Vector3.forward * distance, rotation);
            // Lens shift needs the physical camera. A 16:9 sensor with vertical gate fit keeps Field of View vertical.
            m_Camera.usePhysicalProperties = lensShift != Vector2.zero;
            if (m_Camera.usePhysicalProperties)
            {
                m_Camera.sensorSize = new Vector2(36f, 20.25f);
                m_Camera.gateFit = Camera.GateFitMode.Vertical;
                m_Camera.lensShift = lensShift;
            }
            m_Camera.fieldOfView = Fov;
        }
    }
}
