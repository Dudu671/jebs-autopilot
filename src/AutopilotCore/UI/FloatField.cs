using System.Globalization;
using UnityEngine;

namespace AutopilotCore
{
    public sealed class FloatField
    {
        private string _text;
        private float _value;

        public FloatField(float initial)
        {
            _value = initial;
            _text = initial.ToString("G", CultureInfo.InvariantCulture);
        }

        public float Value
        {
            get { return _value; }
            set
            {
                _value = value;
                _text = value.ToString("G", CultureInfo.InvariantCulture);
            }
        }

        // True when the pilot typed into the field.
        public bool Draw(string label)
        {
            return Draw(label, 110f);
        }

        public bool Draw(string label, float labelWidth)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(labelWidth));
            string next = GUILayout.TextField(_text, GUILayout.MinWidth(80f));
            bool typed = next != _text;
            if (typed)
            {
                _text = next;
                float parsed;
                if (float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    _value = parsed;
                }
            }

            GUILayout.EndHorizontal();
            return typed;
        }
    }
}
