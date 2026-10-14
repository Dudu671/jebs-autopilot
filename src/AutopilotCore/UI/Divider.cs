using UnityEngine;

namespace AutopilotCore
{
    public static class Divider
    {
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.22f);

        public static void Draw()
        {
            GUILayout.Space(6f);
            Rect rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                Color previous = GUI.color;
                GUI.color = LineColor;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = previous;
            }

            GUILayout.Space(6f);
        }
    }
}
