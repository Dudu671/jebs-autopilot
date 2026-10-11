using UnityEngine;

namespace AutopilotCore
{
    public sealed class FlightTape
    {
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
                "BANK", Signed(vessel.GetBank(), "F0") + " deg");
            GUILayout.EndVertical();
        }

        private static void DrawRow(string leftName, string leftValue, string rightName, string rightValue)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(leftName, GUILayout.Width(36f));
            GUILayout.Label(leftValue, GUILayout.Width(90f));
            if (!string.IsNullOrEmpty(rightName))
            {
                GUILayout.Label(rightName, GUILayout.Width(36f));
                GUILayout.Label(rightValue);
            }

            GUILayout.EndHorizontal();
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
