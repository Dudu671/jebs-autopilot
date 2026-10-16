using UnityEngine;

namespace AutopilotCore
{
    public sealed class FlightTape
    {
        private const float NameWidth = 40f;
        private const float ValueWidth = 96f;
        private const float ColumnGap = 32f;

        public void Draw(Vessel vessel)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            if (vessel == null)
            {
                GUILayout.Label("No vessel");
                GUILayout.EndVertical();
                return;
            }

            DrawRow("ALT", vessel.GetAltitudeAslFt().ToString("F0") + " ft",
                "FPM", Signed(vessel.GetVerticalSpeedFpm(), "F0"));
            DrawRow("IAS", vessel.GetIasKt().ToString("F0") + " kt",
                "TAS", vessel.GetTasKt().ToString("F0") + " kt");
            DrawRow("GS", vessel.GetGsKt().ToString("F0") + " kt",
                "AoA", vessel.GetAoA().ToString("F1") + " deg");
            DrawRow("HDG", vessel.GetHeading().ToString("F0") + " deg",
                "TRK", vessel.GetTrack().ToString("F0") + " deg");
            DrawRow("BANK", Signed(vessel.GetBank(), "F0") + " deg",
                "WND", WindText(vessel));
            GUILayout.EndVertical();
        }

        private static void DrawRow(string leftName, string leftValue, string rightName, string rightValue)
        {
            GUILayout.BeginHorizontal();
            DrawCell(leftName, leftValue);
            GUILayout.Space(ColumnGap);
            DrawCell(rightName, rightValue);
            GUILayout.EndHorizontal();
        }

        private static void DrawCell(string name, string value)
        {
            GUILayout.Label(name, GuiStyles.LabelLeft, GUILayout.Width(NameWidth));
            GUILayout.Label(value, GuiStyles.LabelRight, GUILayout.Width(ValueWidth));
        }

        private static string WindText(Vessel vessel)
        {
            Vector2 wind = vessel.GetWindHorizontal();
            float knots = wind.magnitude * Units.KnotsPerMps;
            if (knots < 1f)
            {
                return "calm";
            }

            float from = Mathf.Repeat(Mathf.Atan2(-wind.x, -wind.y) * Mathf.Rad2Deg, 360f);
            return Format.Heading(from) + " / " + knots.ToString("F0") + " kt";
        }

        private static string Signed(float value, string format)
        {
            string body = value.ToString(format);
            if (value > 0f)
            {
                return "+" + body;
            }

            return body;
        }
    }
}
