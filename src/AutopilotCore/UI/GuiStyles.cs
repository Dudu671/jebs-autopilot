using UnityEngine;

namespace AutopilotCore
{
    // Skin styles whose text turns green while switched on. Built lazily inside OnGUI.
    public static class GuiStyles
    {
        private static readonly Color On = new Color(0.3f, 1f, 0.4f);

        // Extra pixels between a checkbox and its label.
        private const int CheckboxGap = 6;

        // Inset of the text drawn over a section header button.
        private const int HeaderInset = 8;

        // Font size assumed when the skin leaves it to the font, and the caption's brightness.
        private const int CaptionBaseSize = 13;
        private const float CaptionDim = 0.7f;

        private static GUISkin _skin;
        private static GUIStyle _toggle;
        private static GUIStyle _toggleButton;
        private static GUIStyle _labelLeft;
        private static GUIStyle _labelRight;
        private static GUIStyle _caption;
        private static GUIStyle _headerTitle;
        private static GUIStyle _headerSummary;
        private static GUIStyle _headerSummaryOn;

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

        // The skin's own label alignment is not left everywhere, so text that must line up says so.
        public static GUIStyle LabelLeft
        {
            get
            {
                Refresh();
                return _labelLeft;
            }
        }

        public static GUIStyle LabelRight
        {
            get
            {
                Refresh();
                return _labelRight;
            }
        }

        // Smaller, dimmer note under a control.
        public static GUIStyle Caption
        {
            get
            {
                Refresh();
                return _caption;
            }
        }

        public static GUIStyle HeaderTitle
        {
            get
            {
                Refresh();
                return _headerTitle;
            }
        }

        public static GUIStyle HeaderSummary(bool on)
        {
            Refresh();
            return on ? _headerSummaryOn : _headerSummary;
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
            _labelLeft = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft };
            _labelRight = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight };
            _caption = new GUIStyle(_labelLeft);
            _caption.fontSize = Mathf.Max(9, (GUI.skin.label.fontSize > 0 ? GUI.skin.label.fontSize : CaptionBaseSize) - 2);
            _caption.normal.textColor = GUI.skin.label.normal.textColor * CaptionDim;
            _caption.margin.top = 0;
            _headerTitle = Header(TextAnchor.MiddleLeft);
            _headerSummary = Header(TextAnchor.MiddleRight);
            _headerSummaryOn = Header(TextAnchor.MiddleRight);
            _headerSummaryOn.normal.textColor = On;
        }

        private static GUIStyle Header(TextAnchor alignment)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label) { alignment = alignment, wordWrap = false };
            style.padding = new RectOffset(HeaderInset, HeaderInset, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            return style;
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
