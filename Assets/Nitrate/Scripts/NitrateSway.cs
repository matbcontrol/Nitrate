using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// Pendulum swing for hanging things (the lantern, the chain, reeds). The shadow follows for free because the
    /// whole transform moves, so there is no vertex wind that the shadow pass could disagree with.
    /// </summary>
    [ExecuteAlways]
    public class NitrateSway : MonoBehaviour
    {
        public Vector3 axis = Vector3.forward;
        public float amplitude = 6f;
        [Tooltip("Seconds per swing. Keep it a divisor of the 15 s loop (3, 3.75, 5, 7.5) so the loop stays seamless.")]
        public float period = 3.75f;
        [Range(0f, 1f)] public float phase;
        [Tooltip("Swing in world space, so a lantern keeps hanging straight down when the arm holding it moves.")]
        public bool worldSpace;
        [Tooltip("0: a plain pendulum. 1: the swing grows in gusts (same gusts as the grass) and leans by Wind Bias.")]
        [Range(0f, 1f)] public float windInfluence;
        [Tooltip("Extra angle in a full gust, degrees (which way it is pushed).")]
        public float windBias;
        [SerializeField, HideInInspector] Quaternion m_Rest = Quaternion.identity;
        [SerializeField, HideInInspector] bool m_HasRest;

        void OnEnable()
        {
            if (!m_HasRest)
            {
                m_Rest = transform.localRotation;
                m_HasRest = true;
            }
        }

        void Update() => Animate();

        public void Animate()
        {
            float angle = amplitude * Mathf.Sin(2f * Mathf.PI * (NitrateClock.Time / period + phase));
            if (windInfluence > 0f)
            {
                float gust = NitrateAtmosphere.Gust(transform.position);
                angle = angle * Mathf.Lerp(1f, 0.4f + 1.2f * gust, windInfluence) + windBias * gust * windInfluence;
            }
            if (worldSpace)
                transform.rotation = Quaternion.AngleAxis(angle, axis);
            else
                transform.localRotation = m_Rest * Quaternion.AngleAxis(angle, axis);
        }
    }
}
