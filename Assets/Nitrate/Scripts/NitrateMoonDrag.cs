using UnityEngine;
using UnityEngine.InputSystem;

namespace Nitrate
{
    /// <summary>
    /// Drag the moon with the right mouse button: the light turns, and every beam, shadow and the moon disc follow.
    /// The drag is clamped near the view axis: the beams scatter forward (Henyey-Greenstein g = 0.6), so far
    /// off-axis they fade and the toy would look broken. C toggles a drawn cursor, because screen recorders
    /// (including Unity Recorder) do not capture the system cursor.
    /// </summary>
    public class NitrateMoonDrag : MonoBehaviour
    {
        public Light moon;
        public float degreesPerPixel = 0.08f;
        [Tooltip("Maximum yaw and pitch away from the starting direction, degrees.")]
        public Vector2 limits = new Vector2(20f, 10f);
        public bool drawCursor;

        Quaternion m_Start;
        Vector2 m_Offset;
        Texture2D m_Cursor;

        void OnEnable()
        {
            if (moon != null)
                m_Start = moon.transform.rotation;
            m_Cursor ??= MakeArrow();
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
            {
                drawCursor = !drawCursor;
                Cursor.visible = !drawCursor;
            }
            if (mouse == null || moon == null || !mouse.rightButton.isPressed)
                return;

            Vector2 delta = mouse.delta.ReadValue() * degreesPerPixel;
            m_Offset.x = Mathf.Clamp(m_Offset.x + delta.x, -limits.x, limits.x);
            m_Offset.y = Mathf.Clamp(m_Offset.y + delta.y, -limits.y, limits.y);
            // Yawing the light around world up turns the moon the same way; pitching the light down raises the moon.
            moon.transform.rotation = Quaternion.AngleAxis(m_Offset.x, Vector3.up) * m_Start * Quaternion.Euler(m_Offset.y, 0f, 0f);
        }

        void OnGUI()
        {
            if (!drawCursor || m_Cursor == null || Mouse.current == null)
                return;
            Vector2 p = Mouse.current.position.ReadValue();
            float scale = Screen.height / 1080f;
            GUI.DrawTexture(new Rect(p.x, Screen.height - p.y, 24f * scale, 24f * scale), m_Cursor);
        }

        void OnDisable() => Cursor.visible = true;

        // A classic arrow cursor drawn into a texture: white fill, black outline.
        static Texture2D MakeArrow()
        {
            const int size = 24;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            Vector2[] shape = { new Vector2(1, 1), new Vector2(1, 19), new Vector2(6, 14), new Vector2(10, 22), new Vector2(13, 20.5f), new Vector2(9, 13), new Vector2(15, 13) };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = Inside(shape, p);
                    bool near = inside || Inside(shape, p + Vector2.right) || Inside(shape, p - Vector2.right) || Inside(shape, p + Vector2.up) || Inside(shape, p - Vector2.up);
                    bool edge = near && !(Inside(shape, p + Vector2.right) && Inside(shape, p - Vector2.right) && Inside(shape, p + Vector2.up) && Inside(shape, p - Vector2.up));
                    Color c = !near ? Color.clear : edge ? Color.black : Color.white;
                    texture.SetPixel(x, size - 1 - y, c); // GUI draws textures top-down
                }
            }
            texture.Apply();
            return texture;
        }

        static bool Inside(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
