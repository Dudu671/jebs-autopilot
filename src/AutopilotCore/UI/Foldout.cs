using System.Collections.Generic;
using UnityEngine;

namespace AutopilotCore
{
    // Collapsible section header showing the section's state on the right. Open state survives restarts.
    public static class Foldout
    {
        private const string PrefPrefix = "JebsAutopilot.Open.";

        private static readonly Dictionary<string, bool> OpenState = new Dictionary<string, bool>();

        // Draws the header and returns whether the section is open. A click takes effect on the next
        // event, so the controls below match the ones laid out for this one.
        public static bool Header(string title, string summary, bool on)
        {
            bool open = IsOpen(title);
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.ExpandWidth(true));
            if (GUI.Button(rect, GUIContent.none))
            {
                OpenState[title] = !open;
                PlayerPrefs.SetInt(PrefPrefix + title, open ? 0 : 1);
                PlayerPrefs.Save();
            }

            GUI.Label(rect, (open ? "-   " : "+   ") + title, GuiStyles.HeaderTitle);
            GUI.Label(rect, summary, GuiStyles.HeaderSummary(on));
            return open;
        }

        private static bool IsOpen(string title)
        {
            bool open;
            if (!OpenState.TryGetValue(title, out open))
            {
                open = PlayerPrefs.GetInt(PrefPrefix + title, 0) != 0;
                OpenState[title] = open;
            }

            return open;
        }
    }
}
