using UnityEngine;

namespace AutopilotCore
{
    // Skin styles whose text turns green while switched on. Built lazily inside OnGUI.
    public static class GuiStyles
    {
        private static readonly Color On = new Color(0.3f, 1f, 0.4f);

        // Extra pixels between a checkbox and its label.
        private const int CheckboxGap = 6;

        private static GUISkin _skin;
        private static GUIStyle _toggle;
        private static GUIStyle _toggleButton;

        public static GUIStyle Toggle
        {
            get
            {
                Refresh();
                return _toggle;
            }
        }

        // Toggle drawn as a button; also the style for toolbars, whose selected cell uses the on state.
        public static GUIStyle ToggleButton
        {
            get
            {
                Refresh();
                return _toggleButton;
            }
        }

        private static void Refresh()
        {
            if (_toggle != null && _skin == GUI.skin)
            {
                return;
            }

            _skin = GUI.skin;
            _toggle = Green(new GUIStyle(GUI.skin.toggle));
            _toggle.padding.left += CheckboxGap;
            _toggleButton = Green(new GUIStyle(GUI.skin.button));
        }

        private static GUIStyle Green(GUIStyle style)
        {
            style.onNormal.textColor = On;
            style.onHover.textColor = On;
            style.onActive.textColor = On;
            style.onFocused.textColor = On;
            return style;
        }
    }
}
