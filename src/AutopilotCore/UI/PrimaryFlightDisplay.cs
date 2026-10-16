using UnityEngine;

namespace AutopilotCore
{
    public sealed class PrimaryFlightDisplay : InstrumentWindow
    {
        private const int CanvasWidth = 520;
        private const int CanvasHeight = 430;
        private const float PxPerPitchDeg = 6f;
        private const float PxPerKt = 4f;
        private const float PxPerFt = 0.2f;
        private const float PxPerHdgDeg = 4f;
        private const float BankRadius = 118f;

        private static readonly Rect Adi = new Rect(140f, 44f, 240f, 280f);
        private static readonly Vector2 AdiCenter = new Vector2(260f, 184f);
        private static readonly Rect SpeedTape = new Rect(62f, 44f, 64f, 280f);
        private static readonly Rect AltTape = new Rect(394f, 44f, 76f, 280f);
        private static readonly Rect Vsi = new Rect(480f, 64f, 32f, 240f);
        private static readonly Rect HdgTape = new Rect(140f, 338f, 240f, 40f);

        private static readonly Color Sky = new Color(0.16f, 0.45f, 0.85f);
        private static readonly Color Ground = new Color(0.52f, 0.32f, 0.14f);
        private static readonly Color TapeBg = new Color(0.22f, 0.23f, 0.25f);
        private static readonly Color White = Color.white;
        private static readonly Color Black = Color.black;
        private static readonly Color Dim = new Color(0.55f, 0.55f, 0.58f);
        private static readonly Color Magenta = new Color(1f, 0.35f, 1f);
        private static readonly Color Green = new Color(0.25f, 1f, 0.35f);
        private static readonly Color Yellow = new Color(1f, 0.85f, 0.1f);

        private static readonly int[] BankMarks = { -60, -45, -30, -20, -10, 10, 20, 30, 45, 60 };
        private static readonly int[] VsiMarks = { 500, 1000, 2000, 6000 };

        public PrimaryFlightDisplay()
            : base(0x4A454255, "PFD", CanvasWidth, CanvasHeight, 60f, 120f)
        {
        }

        protected override void Render(Canvas2D c, FlightData d)
        {
            DrawAttitude(c, d);
            DrawBankScale(c, d);
            DrawAircraftSymbol(c);
            DrawFlightPathVector(c, d);
            DrawSpeedTape(c, d);
            DrawAltitudeTape(c, d);
            DrawVsi(c, d);
            DrawHeadingTape(c, d);
            DrawFma(c, d);
            DrawInfo(c, d);
        }

        private static Vector2 Horizon(float roll, float x, float y)
        {
            return AdiCenter + Canvas2D.Rotate(new Vector2(x, y), roll);
        }

        private static void DrawAttitude(Canvas2D c, FlightData d)
        {
            float roll = -d.Bank;
            float p = d.Pitch * PxPerPitchDeg;

            c.PushClip(Adi);
            c.FillQuad(Horizon(roll, -900f, -900f), Horizon(roll, 900f, -900f), Horizon(roll, 900f, p), Horizon(roll, -900f, p), Sky);
            c.FillQuad(Horizon(roll, -900f, p), Horizon(roll, 900f, p), Horizon(roll, 900f, 900f), Horizon(roll, -900f, 900f), Ground);
            c.Line(Horizon(roll, -900f, p), Horizon(roll, 900f, p), 2f, White);

            c.PushClip(new Rect(Adi.x + 24f, Adi.y + 44f, Adi.width - 48f, Adi.height - 84f));
            for (int i = -36; i <= 36; i++)
            {
                float deg = i * 2.5f;
                if (i == 0 || Mathf.Abs(deg - d.Pitch) > 22f)
                {
                    continue;
                }

                bool ten = i % 4 == 0;
                float half = ten ? 40f : (i % 2 == 0 ? 22f : 10f);
                float y = p - (deg * PxPerPitchDeg);
                c.Line(Horizon(roll, -half, y), Horizon(roll, half, y), 2f, White);
                if (ten)
                {
                    string label = Mathf.Abs(deg).ToString("0");
                    Vector2 left = Horizon(roll, -half - 14f, y);
                    Vector2 right = Horizon(roll, half + 14f, y);
                    c.Text(label, left.x, left.y, 13, White, TextAlign.Center);
                    c.Text(label, right.x, right.y, 13, White, TextAlign.Center);
                }
            }

            c.PopClip();
            c.PopClip();
        }

        private static void DrawBankScale(Canvas2D c, FlightData d)
        {
            c.Arc(AdiCenter, BankRadius, -60f, 60f, 2f, White);
            for (int i = 0; i < BankMarks.Length; i++)
            {
                int a = BankMarks[i];
                float len = (Mathf.Abs(a) == 30 || Mathf.Abs(a) == 60) ? 12f : 7f;
                c.Line(Canvas2D.Polar(AdiCenter, BankRadius, a), Canvas2D.Polar(AdiCenter, BankRadius + len, a), 2f, White);
            }

            c.FillTriangle(
                new Vector2(AdiCenter.x - 7f, AdiCenter.y - BankRadius - 12f),
                new Vector2(AdiCenter.x + 7f, AdiCenter.y - BankRadius - 12f),
                new Vector2(AdiCenter.x, AdiCenter.y - BankRadius),
                White);

            float roll = -Mathf.Clamp(d.Bank, -70f, 70f);
            c.FillTriangle(
                Horizon(roll, 0f, -BankRadius + 1f),
                Horizon(roll, -8f, -BankRadius + 13f),
                Horizon(roll, 8f, -BankRadius + 13f),
                Yellow);
        }

        private static void DrawAircraftSymbol(Canvas2D c)
        {
            float x = AdiCenter.x;
            float y = AdiCenter.y;
            DrawOutlinedRect(c, new Rect(x - 92f, y - 3f, 50f, 6f));
            DrawOutlinedRect(c, new Rect(x - 48f, y - 3f, 6f, 16f));
            DrawOutlinedRect(c, new Rect(x + 42f, y - 3f, 50f, 6f));
            DrawOutlinedRect(c, new Rect(x + 42f, y - 3f, 6f, 16f));
            DrawOutlinedRect(c, new Rect(x - 4f, y - 4f, 8f, 8f));
        }

        private static void DrawOutlinedRect(Canvas2D c, Rect r)
        {
            c.FillRect(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), Black);
            c.FillRect(r, Yellow);
        }

        private static void DrawFlightPathVector(Canvas2D c, FlightData d)
        {
            if (d.TasKt < 20f)
            {
                return;
            }

            float x = Mathf.Clamp(AdiCenter.x + (d.FpvRight * PxPerPitchDeg), Adi.xMin + 24f, Adi.xMax - 24f);
            float y = Mathf.Clamp(AdiCenter.y - (d.FpvUp * PxPerPitchDeg), Adi.yMin + 24f, Adi.yMax - 24f);
            Vector2 p = new Vector2(x, y);
            c.Circle(p, 7f, 2f, Green);
            c.Line(new Vector2(x - 7f, y), new Vector2(x - 19f, y), 2f, Green);
            c.Line(new Vector2(x + 7f, y), new Vector2(x + 19f, y), 2f, Green);
            c.Line(new Vector2(x, y - 7f), new Vector2(x, y - 14f), 2f, Green);
        }

        private static void DrawSpeedTape(Canvas2D c, FlightData d)
        {
            Rect r = SpeedTape;
            float cy = AdiCenter.y;
            float ias = d.IasKt;

            c.FillRect(r, TapeBg);
            c.PushClip(r);
            int start = Mathf.Max(0, Mathf.FloorToInt((ias - 40f) / 10f) * 10);
            for (int v = start; v <= ias + 40f; v += 10)
            {
                float y = cy - ((v - ias) * PxPerKt);
                c.Line(new Vector2(r.xMax - 10f, y), new Vector2(r.xMax, y), 2f, White);
                if (v % 20 == 0)
                {
                    c.Text(v.ToString(), r.xMax - 14f, y, 14, White, TextAlign.Right);
                }
            }

            if (Mathf.Abs(d.IasTrendKt) > 1f)
            {
                float ty = cy - (Mathf.Clamp(d.IasTrendKt, -32f, 32f) * PxPerKt);
                float x = r.xMax - 4f;
                c.Line(new Vector2(x, cy), new Vector2(x, ty), 2f, Green);
                float dir = d.IasTrendKt > 0f ? -1f : 1f;
                c.FillTriangle(new Vector2(x - 5f, ty - (dir * 6f)), new Vector2(x + 5f, ty - (dir * 6f)), new Vector2(x, ty), Green);
            }

            if (d.HasSpeedTarget)
            {
                float by = Mathf.Clamp(cy - ((d.SpeedTargetKt - ias) * PxPerKt), r.yMin + 8f, r.yMax - 8f);
                DrawRightBug(c, r.xMax, by, ModeColor(Magenta, d.SpeedOn));
            }

            c.PopClip();

            Rect box = new Rect(r.x - 4f, cy - 14f, r.width + 4f, 28f);
            c.FillRect(box, Black);
            c.StrokeRect(box, 2f, White);
            c.FillTriangle(new Vector2(box.xMax - 1f, cy - 7f), new Vector2(box.xMax + 8f, cy), new Vector2(box.xMax - 1f, cy + 7f), White);
            c.Text(Mathf.RoundToInt(ias).ToString(), box.xMax - 8f, cy, 18, White, TextAlign.Right);

            if (d.HasSpeedTarget)
            {
                c.Text(d.SpeedTargetKt.ToString("0"), r.center.x, 36f, 15, ModeColor(Magenta, d.SpeedOn), TextAlign.Center);
            }
        }

        private static void DrawAltitudeTape(Canvas2D c, FlightData d)
        {
            Rect r = AltTape;
            float cy = AdiCenter.y;
            float alt = d.AltFt;

            c.FillRect(r, TapeBg);
            c.PushClip(r);
            int start = Mathf.FloorToInt((alt - 750f) / 100f) * 100;
            for (int v = start; v <= alt + 750f; v += 100)
            {
                float y = cy - ((v - alt) * PxPerFt);
                c.Line(new Vector2(r.xMin, y), new Vector2(r.xMin + 10f, y), 2f, White);
                if (v % 200 == 0)
                {
                    c.Text(v.ToString(), r.xMin + 14f, y, 14, White, TextAlign.Left);
                }
            }

            if (d.HasAltitudeTarget)
            {
                float by = Mathf.Clamp(cy - ((d.AltitudeTargetFt - alt) * PxPerFt), r.yMin + 8f, r.yMax - 8f);
                DrawLeftBug(c, r.xMin, by, ModeColor(Magenta, d.AltitudeOn));
            }

            c.PopClip();

            Rect box = new Rect(r.x, cy - 15f, r.width + 4f, 30f);
            c.FillRect(box, Black);
            c.StrokeRect(box, 2f, White);
            c.FillTriangle(new Vector2(box.xMin + 1f, cy - 7f), new Vector2(box.xMin - 8f, cy), new Vector2(box.xMin + 1f, cy + 7f), White);
            c.Text(Mathf.RoundToInt(alt).ToString(), box.xMax - 6f, cy, 18, White, TextAlign.Right);

            if (d.HasAltitudeTarget)
            {
                c.Text(d.AltitudeTargetFt.ToString("0"), r.center.x, 36f, 15, ModeColor(Magenta, d.AltitudeOn), TextAlign.Center);
            }
        }

        private static float VsiOffset(float fpm)
        {
            float v = Mathf.Abs(fpm);
            float offset;
            if (v <= 1000f)
            {
                offset = v * 0.06f;
            }
            else if (v <= 2000f)
            {
                offset = 60f + ((v - 1000f) * 0.03f);
            }
            else
            {
                offset = 90f + (Mathf.Min(v - 2000f, 4000f) * 0.0055f);
            }

            return Mathf.Sign(fpm) * offset;
        }

        private static void DrawVsi(Canvas2D c, FlightData d)
        {
            Rect r = Vsi;
            float cy = AdiCenter.y;
            c.FillRect(r, new Color(0.16f, 0.17f, 0.19f));

            c.Line(new Vector2(r.xMin, cy), new Vector2(r.xMin + 12f, cy), 2f, White);
            for (int i = 0; i < VsiMarks.Length; i++)
            {
                int mark = VsiMarks[i];
                float len = mark == 500 ? 5f : 9f;
                for (int s = -1; s <= 1; s += 2)
                {
                    float y = cy - VsiOffset(s * mark);
                    c.Line(new Vector2(r.xMin, y), new Vector2(r.xMin + len, y), 2f, White);
                    if (mark >= 1000)
                    {
                        c.Text((mark / 1000).ToString(), r.xMin + 18f, y, 12, Dim, TextAlign.Center);
                    }
                }
            }

            c.PushClip(r);
            c.Line(new Vector2(r.xMax + 24f, cy), new Vector2(r.xMin + 2f, cy - VsiOffset(d.VsFpm)), 3f, White);
            c.PopClip();

            if (Mathf.Abs(d.VsFpm) >= 400f)
            {
                string text = (Mathf.RoundToInt(d.VsFpm / 50f) * 50).ToString();
                float y = d.VsFpm > 0f ? r.yMin - 10f : r.yMax + 10f;
                c.Text(text, r.center.x, y, 13, White, TextAlign.Center);
            }
        }

        private static void DrawHeadingTape(Canvas2D c, FlightData d)
        {
            Rect r = HdgTape;
            float cx = r.center.x;
            float hdg = d.Heading;

            c.FillRect(r, TapeBg);
            c.PushClip(r);
            int start = Mathf.FloorToInt((hdg - 32f) / 5f) * 5;
            for (int a = start; a <= hdg + 32f; a += 5)
            {
                float x = cx + ((a - hdg) * PxPerHdgDeg);
                int bearing = ((a % 360) + 360) % 360;
                bool ten = bearing % 10 == 0;
                c.Line(new Vector2(x, r.yMin), new Vector2(x, r.yMin + (ten ? 10f : 6f)), 2f, White);
                if (ten)
                {
                    c.Text(HeadingLabel(bearing), x, r.yMin + 24f, 14, White, TextAlign.Center);
                }
            }

            float trackX = cx + (Mathf.DeltaAngle(hdg, d.Track) * PxPerHdgDeg);
            if (d.GsKt > 5f)
            {
                Vector2 t = new Vector2(trackX, r.yMin + 8f);
                c.Line(t + new Vector2(0f, -6f), t + new Vector2(6f, 0f), 2f, Green);
                c.Line(t + new Vector2(6f, 0f), t + new Vector2(0f, 6f), 2f, Green);
                c.Line(t + new Vector2(0f, 6f), t + new Vector2(-6f, 0f), 2f, Green);
                c.Line(t + new Vector2(-6f, 0f), t + new Vector2(0f, -6f), 2f, Green);
            }

            float bugBearing = 0f;
            bool showBug = false;
            if (d.HasNavTrack)
            {
                bugBearing = d.NavTrack;
                showBug = true;
            }
            else if (d.HasHeadingTarget)
            {
                bugBearing = d.HeadingTarget;
                showBug = true;
            }

            if (showBug)
            {
                float bx = Mathf.Clamp(cx + (Mathf.DeltaAngle(hdg, bugBearing) * PxPerHdgDeg), r.xMin + 8f, r.xMax - 8f);
                Color color = ModeColor(Magenta, d.HasNavTrack || d.HeadingOn);
                c.FillRect(new Rect(bx - 8f, r.yMin, 5f, 6f), color);
                c.FillRect(new Rect(bx + 3f, r.yMin, 5f, 6f), color);
                c.FillRect(new Rect(bx - 8f, r.yMin, 16f, 2f), color);
            }

            c.PopClip();

            c.Line(new Vector2(cx, r.yMin - 6f), new Vector2(cx, r.yMin + 14f), 3f, Yellow);
            Rect box = new Rect(cx - 28f, r.yMax + 4f, 56f, 24f);
            c.FillRect(box, Black);
            c.StrokeRect(box, 2f, White);
            c.Text(Format.Heading(hdg), cx, box.center.y, 16, White, TextAlign.Center);
        }

        private static string HeadingLabel(int bearing)
        {
            switch (bearing)
            {
                case 0: return "N";
                case 90: return "E";
                case 180: return "S";
                case 270: return "W";
                default: return (bearing / 10).ToString();
            }
        }

        private static void DrawFma(Canvas2D c, FlightData d)
        {
            c.FillRect(new Rect(0f, 0f, CanvasWidth, 28f), new Color(0.02f, 0.02f, 0.03f));
            DrawFmaCell(c, 0, d.SpeedOn, "SPD", d.SpeedTargetKt.ToString("0"));
            if (d.ProcedureOn)
            {
                DrawFmaCell(c, 1, true, d.ProcedureLateral, d.ProcedureLateralValue);
                DrawFmaCell(c, 2, true, d.ProcedureVertical, "");
                DrawFmaCell(c, 3, false, "PIT", "");
                return;
            }

            if (d.NavOn)
            {
                DrawFmaCell(c, 1, true, "NAV", d.NavLabel);
            }
            else if (d.HoldOn)
            {
                DrawFmaCell(c, 1, true, "HLD", d.HoldLabel);
            }
            else
            {
                DrawFmaCell(c, 1, d.HeadingOn, "HDG", Format.Heading(d.HeadingTarget));
            }
            DrawFmaCell(c, 2, d.AltitudeOn, "ALT", d.AltitudeTargetFt.ToString("0"));
            DrawFmaCell(c, 3, d.PitchOn, "PIT", d.PitchTarget.ToString("0"));
        }

        private static void DrawFmaCell(Canvas2D c, int index, bool on, string mode, string value)
        {
            float width = CanvasWidth / 4f;
            float x = index * width;
            if (index > 0)
            {
                c.Line(new Vector2(x, 3f), new Vector2(x, 25f), 1f, Dim);
            }

            string text = on ? mode + " " + value : mode;
            c.Text(text, x + (width * 0.5f), 14f, 15, on ? Green : new Color(0.35f, 0.35f, 0.38f), TextAlign.Center);
        }

        private static void DrawInfo(Canvas2D c, FlightData d)
        {
            c.Text("TAS " + d.TasKt.ToString("0"), 10f, 350f, 14, White, TextAlign.Left);
            c.Text("GS  " + d.GsKt.ToString("0"), 10f, 372f, 14, White, TextAlign.Left);
            c.Text("M " + d.Mach.ToString("0.00"), 10f, 394f, 14, White, TextAlign.Left);
            c.Text("AOA " + d.AoA.ToString("0.0"), 510f, 350f, 14, White, TextAlign.Right);
            c.Text("OAT " + Format.Signed(d.OatC, "0") + "°C", 510f, 372f, 14, White, TextAlign.Right);
        }

        private static void DrawRightBug(Canvas2D c, float x, float y, Color color)
        {
            c.FillRect(new Rect(x - 6f, y - 8f, 6f, 5f), color);
            c.FillRect(new Rect(x - 6f, y + 3f, 6f, 5f), color);
            c.FillRect(new Rect(x - 2f, y - 8f, 2f, 16f), color);
        }

        private static void DrawLeftBug(Canvas2D c, float x, float y, Color color)
        {
            c.FillRect(new Rect(x, y - 8f, 6f, 5f), color);
            c.FillRect(new Rect(x, y + 3f, 6f, 5f), color);
            c.FillRect(new Rect(x, y - 8f, 2f, 16f), color);
        }

        private static Color ModeColor(Color color, bool on)
        {
            color.a = on ? 1f : 0.45f;
            return color;
        }
    }
}
