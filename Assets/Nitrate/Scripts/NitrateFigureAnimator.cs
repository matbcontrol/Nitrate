using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Brings the Lamplighter to life without a skeleton: he breathes (a slow stretch from the feet), sways a little,
    /// and as the camera pulls back he lifts the lantern toward the mill, then lowers it as the view closes again.
    /// Every period divides the 15 s loop, so the recorded loop stays seamless.
    /// </summary>
    [ExecuteAlways]
    public class NitrateFigureAnimator : MonoBehaviour
    {
        [Tooltip("Coat, head and legs. Pivot at the feet.")]
        public Transform body;
        [Tooltip("Arms and pole. Pivot at the shoulder.")]
        public Transform arms;

        [Tooltip("Breathing stretch of the body (fraction of its height).")]
        public float breathe = 0.008f;
        public float breathPeriod = 3.75f;
        [Tooltip("Slow sway around the feet, degrees.")]
        public float sway = 1.2f;
        public float swayPeriod = 7.5f;
        [Tooltip("How far the lantern is lifted toward the mill while the view opens up, degrees.")]
        public float raise = 16f;
        [Tooltip("Lift over one loop (x: 0..1 of the loop). Same shape as the dolly zoom, so the gesture follows the reveal.")]
        public AnimationCurve raiseCurve = NitrateDollyZoom.DefaultLoop();

        void Update() => Animate();

        public void Animate()
        {
            if (body == null || arms == null)
                return;
            float t = NitrateClock.Time;
            float breath = Mathf.Sin(2f * Mathf.PI * t / breathPeriod);
            body.localScale = new Vector3(1f, 1f + breathe * breath, 1f);
            body.localRotation = Quaternion.Euler(0f, 0f, sway * Mathf.Sin(2f * Mathf.PI * t / swayPeriod));
            float lift = raise * raiseCurve.Evaluate(NitrateClock.LoopPhase);
            arms.localRotation = Quaternion.Euler(0f, 0f, lift + 1.5f * breath);
        }
    }
}
