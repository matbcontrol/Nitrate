using UnityEngine;

namespace Nitrate
{
    /// <summary>
    /// One clock for everything that moves, so the hero shot loops without a seam: the camera loop, the sails,
    /// the lantern, the scarf and the figure all use periods that divide the loop length. Restart() puts everything
    /// back to frame 1. In the editor the clock also runs outside Play mode (Nitrate > Animate In Edit Mode).
    /// </summary>
    public static class NitrateClock
    {
        public const float LoopSeconds = 15f;

        static float s_Start;

#if UNITY_EDITOR
        /// <summary>Animate the scene in edit mode too. Toggled from the Nitrate menu.</summary>
        public static bool AnimateInEditMode = true;

        /// <summary>When 0 or more, the edit-mode clock is pinned to this time (offline renders of one moment).</summary>
        public static float Pinned = -1f;
#endif

        public static float Time
        {
            get
            {
                if (Application.isPlaying)
                    return UnityEngine.Time.time - s_Start;
#if UNITY_EDITOR
                if (Pinned >= 0f)
                    return Pinned;
                // Wrapped to an hour so the float keeps its precision in a long editor session.
                return AnimateInEditMode ? (float)(UnityEditor.EditorApplication.timeSinceStartup % 3600.0) : 0f;
#else
                return 0f;
#endif
            }
        }

        /// <summary>0..1 position inside the current loop.</summary>
        public static float LoopPhase => Mathf.Repeat(Time / LoopSeconds, 1f);

        public static void Restart() => s_Start = UnityEngine.Time.time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => s_Start = 0f;
    }
}
