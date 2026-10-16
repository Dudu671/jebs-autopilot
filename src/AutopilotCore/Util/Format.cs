using UnityEngine;

namespace AutopilotCore
{
    public static class Format
    {
        // Built once: the instruments format headings every frame.
        private static readonly string[] Headings = BuildHeadings();

        public static string Heading(float heading)
        {
            int rounded = Mathf.RoundToInt(Mathf.Repeat(heading, 360f)) % 360;
            // A NaN heading rounds to int.MinValue.
            return rounded >= 0 ? Headings[rounded] : rounded.ToString("000");
        }

        private static string[] BuildHeadings()
        {
            string[] headings = new string[360];
            for (int i = 0; i < headings.Length; i++)
            {
                headings[i] = i.ToString("000");
            }

            return headings;
        }

        public static string LatLon(double lat, double lon)
        {
            lon = ((lon + 540.0) % 360.0) - 180.0;
            return System.Math.Abs(lat).ToString("0.0000") + "°" + (lat >= 0.0 ? "N" : "S") + "  " +
                   System.Math.Abs(lon).ToString("0.0000") + "°" + (lon >= 0.0 ? "E" : "W");
        }

        // h:mm:ss above one hour, otherwise mm:ss.
        public static string Duration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int hours = total / 3600;
            int minutes = (total / 60) % 60;
            int secs = total % 60;
            string mmss = minutes.ToString("00") + ":" + secs.ToString("00");
            return hours > 0 ? hours + ":" + mmss : mmss;
        }

        public static string Signed(float value, string format)
        {
            string body = value.ToString(format);
            return value > 0f ? "+" + body : body;
        }
    }
}
