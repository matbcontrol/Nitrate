using UnityEditor;
using UnityEngine;

namespace Nitrate.EditorTools
{
    /// <summary>
    /// Makes the Mill scene animate in edit mode: about 30 times a second it moves the sails, lantern, scarf and figure,
    /// steps the film clock and repaints the views, without Play mode. It drives the components directly instead of
    /// relying on edit-mode Update, which Unity skips while its window is in the background.
    /// Only while a scene with a NitrateRotor is open. Switch it off in Nitrate > Animate In Edit Mode.
    /// </summary>
    [InitializeOnLoad]
    static class NitrateEditorTicker
    {
        const string k_Pref = "Nitrate.AnimateInEditMode";
        const string k_Menu = "Nitrate/Animate In Edit Mode";
        static double s_LastTick;

        static NitrateEditorTicker()
        {
            NitrateClock.AnimateInEditMode = EditorPrefs.GetBool(k_Pref, true);
            EditorApplication.update += Tick;
            EditorApplication.delayCall += () => Menu.SetChecked(k_Menu, NitrateClock.AnimateInEditMode);
        }

        static void Tick()
        {
            if (Application.isPlaying || !NitrateClock.AnimateInEditMode)
                return;
            double now = EditorApplication.timeSinceStartup;
            if (now - s_LastTick < 1.0 / 30.0)
                return;
            s_LastTick = now;
            var rotors = Object.FindObjectsByType<NitrateRotor>(FindObjectsInactive.Exclude);
            if (rotors.Length == 0)
                return;
            foreach (var r in rotors) r.Animate();
            foreach (var s in Object.FindObjectsByType<NitrateSway>(FindObjectsInactive.Exclude)) s.Animate();
            foreach (var f in Object.FindObjectsByType<NitrateFigureAnimator>(FindObjectsInactive.Exclude)) f.Animate();
            foreach (var a in Object.FindObjectsByType<NitrateAtmosphere>(FindObjectsInactive.Exclude)) a.Apply();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        [MenuItem(k_Menu)]
        static void Toggle()
        {
            NitrateClock.AnimateInEditMode = !NitrateClock.AnimateInEditMode;
            EditorPrefs.SetBool(k_Pref, NitrateClock.AnimateInEditMode);
            Menu.SetChecked(k_Menu, NitrateClock.AnimateInEditMode);
        }
    }
}
