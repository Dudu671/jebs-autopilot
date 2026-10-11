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
        }

        public void Draw(string label)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110f));
            string next = GUILayout.TextField(_text, GUILayout.MinWidth(80f));
            if (next != _text)
            {
                _text = next;
                float parsed;
                if (float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    _value = parsed;
                }
            }

            GUILayout.EndHorizontal();
        }
    }
}
