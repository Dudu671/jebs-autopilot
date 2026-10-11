using UnityEngine;

namespace AutopilotCore
{
    public sealed class NavigationDisplay : InstrumentWindow
    {
        private const float RingRadius = 188f;
        private const float MapBudgetMs = 4f;
        // Map covers more than the ring so the corners stay filled when heading-up rotates it.
        private const float MapCoverage = 1.5f;

        private static readonly float[] RangesKm = { 5f, 10f, 20f, 50f, 100f, 200f, 500f };
        private static readonly string[] CompassLabels = { "N", "3", "6", "E", "12", "15", "S", "21", "24", "W", "30", "33" };
        private static readonly Rect MapArea = new Rect(6f, 60f, 448f, 414f);
        private static readonly Vector2 AircraftPos = new Vector2(230f, 267f);

        private static readonly Color Phosphor = new Color(0.4f, 1f, 0.55f);
        private static readonly Color DimPhosphor = new Color(0.25f, 0.6f, 0.35f);
        private static readonly Color White = Color.white;
        private static readonly Color Magenta = new Color(1f, 0.35f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.85f, 0.1f);

        private static readonly Color Cyan = new Color(0.3f, 0.9f, 1f);

        private readonly TerrainMap _map = new TerrainMap();
        private readonly NavHold _nav;
        private readonly NavPlan _plan;
        private int _rangeIndex = 3;
        private bool _headingUp = true;
        private bool _addMode;

        // View state from the last render, used to turn map clicks into coordinates.
        private bool _hasView;
        private double _viewRadius;
        private double _viewLat;
        private double _viewLon;
        private float _viewRot;
        private float _viewPxPerM;
        private float _viewGsKt;
        private float[] _routeNm = new float[0];

        public NavigationDisplay(NavHold nav)
            : base(0x4A454256, "Navigation Display", 460, 480, 620f, 120f)
        {
            _nav = nav;
            _plan = nav != null ? nav.Plan : null;
        }

        protected override Color Background
        {
            get { return new Color(0f, 0.03f, 0.015f, 1f); }
        }

        protected override void Render(Canvas2D c, FlightData d)
        {
            float rangeKm = RangesKm[_rangeIndex];
            float rangeM = rangeKm * 1000f;
            float pxPerM = RingRadius / rangeM;
            float rot = _headingUp ? -d.Heading : 0f;

            _map.Update(d.Body, d.Latitude, d.Longitude, rangeM * MapCoverage, MapBudgetMs);

            _hasView = d.Body != null;
            _viewRadius = d.Body != null ? d.Body.Radius : 0.0;
            _viewLat = d.Latitude;
            _viewLon = d.Longitude;
            _viewRot = rot;
            _viewPxPerM = pxPerM;
            _viewGsKt = d.GsKt;

            c.PushClip(MapArea);
            DrawMap(c, d, pxPerM, rot);
            c.Circle(AircraftPos, RingRadius * 0.5f, 1.5f, DimPhosphor);
            DrawCompassRing(c, rot);
            DrawTrackLine(c, d, rot);
            DrawHeadingBug(c, d, rot);
            DrawRoute(c);
            Vector2 half = Canvas2D.Polar(AircraftPos, RingRadius * 0.5f, -45f);
            c.Text(FormatRange(rangeKm * 0.5f), half.x - 6f, half.y - 8f, 12, DimPhosphor, TextAlign.Right);
            c.PopClip();

            if (_headingUp)
            {
                c.FillTriangle(
                    new Vector2(AircraftPos.x - 8f, AircraftPos.y - RingRadius - 14f),
                    new Vector2(AircraftPos.x + 8f, AircraftPos.y - RingRadius - 14f),
                    new Vector2(AircraftPos.x, AircraftPos.y - RingRadius - 2f),
                    White);
            }

            DrawAircraft(c, d.Heading + rot);
            DrawHeader(c, d, rangeKm);

            if (_map.Building)
            {
                c.Text("MAP " + Mathf.RoundToInt(_map.Progress * 100f) + "%", MapArea.xMin + 8f, MapArea.yMax - 10f, 12, DimPhosphor, TextAlign.Left);
            }

            if (_addMode)
            {
                c.Text("CLICK MAP TO ADD WAYPOINT", MapArea.xMax - 8f, MapArea.yMax - 10f, 12, Cyan, TextAlign.Right);
            }
        }

        protected override void DrawControls()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-", GUILayout.Width(30f)) && _rangeIndex < RangesKm.Length - 1)
            {
                _rangeIndex++;
            }

            GUILayout.Label(FormatRange(RangesKm[_rangeIndex]) + " km", GUILayout.Width(60f));
            if (GUILayout.Button("+", GUILayout.Width(30f)) && _rangeIndex > 0)
            {
                _rangeIndex--;
            }

            GUILayout.FlexibleSpace();
            _headingUp = GUILayout.Toggle(_headingUp, "Heading up");
            GUILayout.EndHorizontal();

            if (_plan != null)
            {
                DrawPlanControls();
            }
        }

        protected override bool OnCanvasClick(Vector2 point, int button)
        {
            if (!_addMode || button != 0 || _plan == null || !_hasView || !MapArea.Contains(point))
            {
                return false;
            }

            Vector2 local = Canvas2D.Rotate(point - AircraftPos, -_viewRot);
            double lat;
            double lon;
            NavMath.FromLocal(_viewRadius, _viewLat, _viewLon, local.x / _viewPxPerM, -local.y / _viewPxPerM, out lat, out lon);
            _plan.Add(lat, lon);
            return true;
        }

        private void DrawPlanControls()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            _addMode = GUILayout.Toggle(_addMode, "Add waypoints", GUI.skin.button);
            if (_plan.Count > 0 && GUILayout.Button("Clear all", GUILayout.Width(80f)))
            {
                _plan.Clear();
            }

            GUILayout.EndHorizontal();

            if (_hasView)
            {
                UpdateRouteDistances();
            }

            for (int i = 0; i < _plan.Count; i++)
            {
                Waypoint wp = _plan[i];
                GUILayout.BeginHorizontal();
                string marker = i == _plan.ActiveIndex ? "> " : "   ";
                string info = marker + NavPlan.NameOf(i) + "   " + Format.LatLon(wp.Latitude, wp.Longitude);
                string route = RouteInfo(i);
                if (route.Length > 0)
                {
                    info += "\n      " + route;
                }

                GUILayout.Label(info);
                if (GUILayout.Button("Go", GUILayout.Width(36f)))
                {
                    _plan.SetActive(i);
                }

                if (GUILayout.Button("X", GUILayout.Width(26f)))
                {
                    _plan.RemoveAt(i);
                    GUILayout.EndHorizontal();
                    break;
                }

                GUILayout.EndHorizontal();
            }
        }

        // Fills _routeNm with distance along the route from the aircraft; passed waypoints get -1.
        private void UpdateRouteDistances()
        {
            if (_routeNm.Length != _plan.Count)
            {
                _routeNm = new float[_plan.Count];
            }

            double total = 0.0;
            double fromLat = _viewLat;
            double fromLon = _viewLon;
            for (int i = 0; i < _plan.Count; i++)
            {
                if (i < _plan.ActiveIndex)
                {
                    _routeNm[i] = -1f;
                    continue;
                }

                Waypoint wp = _plan[i];
                total += NavMath.Distance(_viewRadius, fromLat, fromLon, wp.Latitude, wp.Longitude);
                _routeNm[i] = (float)(total / Units.MetersPerNm);
                fromLat = wp.Latitude;
                fromLon = wp.Longitude;
            }
        }

        private string RouteInfo(int index)
        {
            if (!_hasView || index >= _routeNm.Length || _routeNm[index] < 0f)
            {
                return "";
            }

            float nm = _routeNm[index];
            string ete = _viewGsKt > 5f ? Format.Duration(nm / _viewGsKt * 3600f) : "--:--";
            return nm.ToString(nm < 100f ? "0.0" : "0") + " NM   ETE " + ete;
        }

        private Vector2 ToScreen(double lat, double lon)
        {
            Vector2 offset = NavMath.LocalOffset(_viewRadius, _viewLat, _viewLon, lat, lon);
            Vector2 local = new Vector2(offset.x * _viewPxPerM, -offset.y * _viewPxPerM);
            return AircraftPos + Canvas2D.Rotate(local, _viewRot);
        }

        private void DrawRoute(Canvas2D c)
        {
            if (_plan == null || _plan.Count == 0 || !_hasView)
            {
                return;
            }

            UpdateRouteDistances();

            if (_plan.HasActive)
            {
                double fromLat;
                double fromLon;
                Vector2 from = _nav.TryGetLegStart(out fromLat, out fromLon) ? ToScreen(fromLat, fromLon) : AircraftPos;
                c.Line(from, ToScreen(_plan.Active.Latitude, _plan.Active.Longitude), 2f, Magenta);
            }

            for (int i = 1; i < _plan.Count; i++)
            {
                Color legColor = i <= _plan.ActiveIndex ? DimPhosphor : White;
                if (i == _plan.ActiveIndex)
                {
                    continue;
                }

                c.Line(ToScreen(_plan[i - 1].Latitude, _plan[i - 1].Longitude), ToScreen(_plan[i].Latitude, _plan[i].Longitude), 1.5f, legColor);
            }

            for (int i = 0; i < _plan.Count; i++)
            {
                Waypoint wp = _plan[i];
                Vector2 p = ToScreen(wp.Latitude, wp.Longitude);
                Color color = i == _plan.ActiveIndex ? Magenta : (i < _plan.ActiveIndex ? DimPhosphor : Cyan);
                c.Line(p + new Vector2(0f, -7f), p + new Vector2(7f, 0f), 2f, color);
                c.Line(p + new Vector2(7f, 0f), p + new Vector2(0f, 7f), 2f, color);
                c.Line(p + new Vector2(0f, 7f), p + new Vector2(-7f, 0f), 2f, color);
                c.Line(p + new Vector2(-7f, 0f), p + new Vector2(0f, -7f), 2f, color);
                c.Text(NavPlan.NameOf(i), p.x + 11f, p.y - 7f, 12, color, TextAlign.Left);
                c.Text(Format.LatLon(wp.Latitude, wp.Longitude), p.x + 11f, p.y + 7f, 10, DimPhosphor, TextAlign.Left);
                string route = RouteInfo(i);
                if (route.Length > 0)
                {
                    c.Text(route, p.x + 11f, p.y + 19f, 10, DimPhosphor, TextAlign.Left);
                }
            }
        }

        protected override void OnDispose()
        {
            _map.Dispose();
        }

        private void DrawMap(Canvas2D c, FlightData d, float pxPerM, float rot)
        {
            if (!_map.HasMap || _map.Body != d.Body)
            {
                return;
            }

            Vector2 offset = _map.OffsetMeters(d.Latitude, d.Longitude);
            Vector2 local = new Vector2(-offset.x * pxPerM, offset.y * pxPerM);
            Vector2 center = AircraftPos + Canvas2D.Rotate(local, rot);
            float size = 2f * _map.HalfSizeM * pxPerM;
            c.DrawTexture(_map.Texture, center, new Vector2(size, size), rot, Color.white);
        }

        private static void DrawCompassRing(Canvas2D c, float rot)
        {
            c.Circle(AircraftPos, RingRadius, 2f, Phosphor);
            for (int b = 0; b < 360; b += 5)
            {
                float a = b + rot;
                bool major = b % 30 == 0;
                bool ten = b % 10 == 0;
                float len = major ? 14f : (ten ? 9f : 5f);
                c.Line(Canvas2D.Polar(AircraftPos, RingRadius, a), Canvas2D.Polar(AircraftPos, RingRadius - len, a), ten ? 2f : 1.5f, Phosphor);
                if (major)
                {
                    Vector2 p = Canvas2D.Polar(AircraftPos, RingRadius - 26f, a);
                    c.Text(CompassLabels[b / 30], p.x, p.y, 14, White, TextAlign.Center);
                }
            }
        }

        private static void DrawTrackLine(Canvas2D c, FlightData d, float rot)
        {
            if (d.GsKt < 5f)
            {
                return;
            }

            float a = d.Track + rot;
            c.DashedLine(AircraftPos, Canvas2D.Polar(AircraftPos, RingRadius, a), 2f, 10f, 6f, White);
            Vector2 tick = Canvas2D.Polar(AircraftPos, RingRadius * 0.5f, a);
            Vector2 side = Canvas2D.Rotate(new Vector2(7f, 0f), a);
            c.Line(tick - side, tick + side, 2f, White);
        }

        private static void DrawHeadingBug(Canvas2D c, FlightData d, float rot)
        {
            if (!d.HasHeadingTarget)
            {
                return;
            }

            float a = d.HeadingTarget + rot;
            Color color = Magenta;
            color.a = d.HeadingOn ? 1f : 0.45f;
            c.FillTriangle(
                Canvas2D.Polar(AircraftPos, RingRadius + 6f, a - 3f),
                Canvas2D.Polar(AircraftPos, RingRadius + 6f, a + 3f),
                Canvas2D.Polar(AircraftPos, RingRadius - 8f, a),
                color);
            if (d.HeadingOn)
            {
                c.DashedLine(AircraftPos, Canvas2D.Polar(AircraftPos, RingRadius - 8f, a), 1.5f, 6f, 6f, color);
            }
        }

        private static void DrawAircraft(Canvas2D c, float angle)
        {
            Quad(c, angle, -3f, -16f, 3f, 14f);
            Quad(c, angle, -17f, -3f, 17f, 3f);
            Quad(c, angle, -8f, 10f, 8f, 14f);
        }

        private static void Quad(Canvas2D c, float angle, float x0, float y0, float x1, float y1)
        {
            c.FillQuad(
                AircraftPos + Canvas2D.Rotate(new Vector2(x0, y0), angle),
                AircraftPos + Canvas2D.Rotate(new Vector2(x1, y0), angle),
                AircraftPos + Canvas2D.Rotate(new Vector2(x1, y1), angle),
                AircraftPos + Canvas2D.Rotate(new Vector2(x0, y1), angle),
                Yellow);
        }

        private void DrawHeader(Canvas2D c, FlightData d, float rangeKm)
        {
            c.FillRect(new Rect(0f, 0f, c.Width, 54f), new Color(0f, 0f, 0f, 1f));
            c.Line(new Vector2(0f, 54f), new Vector2(c.Width, 54f), 1f, DimPhosphor);

            c.Text("GS " + d.GsKt.ToString("0"), 12f, 17f, 18, White, TextAlign.Left);
            c.Text("TAS " + d.TasKt.ToString("0"), 100f, 17f, 14, DimPhosphor, TextAlign.Left);
            c.Text("OAT " + Format.Signed(d.OatC, "0") + "°C", 12f, 40f, 14, White, TextAlign.Left);

            string bodyName = d.Body != null ? d.Body.bodyName.ToUpperInvariant() : "";
            c.Text(bodyName, c.Width * 0.5f, 17f, 14, Phosphor, TextAlign.Center);
            c.Text(FormatRange(rangeKm) + " KM  " + (_headingUp ? "HDG UP" : "N UP"), c.Width * 0.5f, 40f, 12, DimPhosphor, TextAlign.Center);

            c.Text("HDG " + Format.Heading(d.Heading), c.Width - 12f, 17f, 18, White, TextAlign.Right);
            c.Text("TRK " + Format.Heading(d.Track), c.Width - 12f, 40f, 18, Phosphor, TextAlign.Right);
        }

        private static string FormatRange(float km)
        {
            return km < 10f ? km.ToString("0.#") : km.ToString("0");
        }
    }
}
