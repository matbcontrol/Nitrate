using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Turns this transform around a local axis by a whole number of turns per loop (the windmill sails).
    /// Four identical sails turned by any multiple of 90 degrees look the same, so the loop has no seam.
    /// Put it on an empty pivot with zero rotation; the pivot's rotation is fully driven.
    /// </summary>
    [ExecuteAlways]
    public class NitrateRotor : MonoBehaviour
    {
        public Vector3 axis = Vector3.forward;
        [Tooltip("Degrees per loop. Use a multiple of 90 for four identical sails.")]
        public float degreesPerLoop = -360f;
        [Tooltip("Angle at the start of the loop. Set it so that a sail crosses the moon in the first second.")]
        public float phase;

        void Update() => Animate();

        public void Animate()
        {
            float time = NitrateClock.Time; // runs in edit mode too
            transform.localRotation = Quaternion.AngleAxis(phase + degreesPerLoop * time / NitrateClock.LoopSeconds, axis);
        }
    }
}
