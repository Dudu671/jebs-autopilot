using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutopilotCore
{
    // Great-circle route drawn over the stock map, 20 km above sea level, with a diamond on each waypoint.
    public sealed class MapRouteOverlay : System.IDisposable
    {
        private const double RouteAltitudeM = 20000.0;
        private const float LineWidthPx = 3f;
        private const float DiamondPx = 8f;

        private static readonly Color Past = new Color(0.25f, 0.6f, 0.35f, 1f);
        private static readonly Color Future = Color.white;
        private static readonly Color Active = new Color(1f, 0.35f, 1f, 1f);
        private static readonly Color FuturePoint = new Color(0.3f, 0.9f, 1f, 1f);

        private readonly List<Vector2> _run = new List<Vector2>(64);
        private NavHold _nav;
        private GUIStyle _label;
        private MapRouteHook _hook;
        private Vessel _vessel;

        public void Sync(Vessel vessel, NavHold nav)
        {
            _vessel = vessel;
            _nav = nav;
            AttachHook();
        }

        public void DrawGui(Camera camera)
        {
            NavPlan plan = Plan;
            if (Event.current == null || Event.current.type != EventType.Repaint || !MapView.MapIsEnabled)
            {
                return;
            }

            if (plan == null || plan.Count == 0 || _vessel == null || _vessel.mainBody == null || camera == null)
            {
                return;
            }

            CelestialBody body = _vessel.mainBody;
            if (plan.HasActive)
            {
                double fromLat = _vessel.latitude;
                double fromLon = _vessel.longitude;
                double legLat;
                double legLon;
                if (_nav.TryGetLegStart(out legLat, out legLon))
                {
                    fromLat = legLat;
                    fromLon = legLon;
                }

                DrawArc(body, camera, fromLat, fromLon, plan.Active.Latitude, plan.Active.Longitude, Active);
            }

            for (int i = 1; i < plan.Count; i++)
            {
                if (i == plan.ActiveIndex)
                {
                    continue;
                }

                Color color = i < plan.ActiveIndex ? Past : Future;
                DrawArc(body, camera, plan[i - 1].Latitude, plan[i - 1].Longitude, plan[i].Latitude, plan[i].Longitude, color);
            }

            for (int i = 0; i < plan.Count; i++)
            {
                DrawDiamond(body, camera, plan[i].Latitude, plan[i].Longitude, PointColor(plan, i));
            }

            DrawLabels(body, camera, plan);
        }

        public void Dispose()
        {
            if (_hook != null)
            {
                UnityEngine.Object.Destroy(_hook);
                _hook = null;
            }
        }

        private NavPlan Plan
        {
            get { return _nav != null ? _nav.Plan : null; }
        }

        private void AttachHook()
        {
            Camera camera = PlanetariumCamera.Camera;
            if (camera == null && MapView.MapCamera != null)
            {
                camera = MapView.MapCamera.GetComponent<Camera>();
            }

            if (camera == null || (_hook != null && _hook.gameObject == camera.gameObject))
            {
                return;
            }

            if (_hook != null)
            {
                UnityEngine.Object.Destroy(_hook);
            }

            _hook = camera.gameObject.AddComponent<MapRouteHook>();
            _hook.Overlay = this;
        }

        private void DrawArc(CelestialBody body, Camera camera, double lat1, double lon1, double lat2, double lon2, Color color)
        {
            Vector3d radialA = body.GetWorldSurfacePosition(lat1, lon1, RouteAltitudeM) - body.position;
            Vector3d radialB = body.GetWorldSurfacePosition(lat2, lon2, RouteAltitudeM) - body.position;
            float degrees = (float)Vector3d.Angle(radialA, radialB);
            int steps = Mathf.Clamp(Mathf.CeilToInt(degrees / 2f), 1, 180);
            _run.Clear();
            for (int i = 0; i <= steps; i++)
            {
                // Vector3d.Slerp is an unimplemented icall in this KSP build and aborts OnGUI.
                Vector3d radial = SlerpRadial(radialA, radialB, i / (double)steps);
                Vector2 screen;
                if (TryProject(body, camera, body.position + radial, out screen))
                {
                    _run.Add(screen);
                }
                else if (_run.Count > 0)
                {
                    Stroke(_run, color);
                    _run.Clear();
                }
            }

            Stroke(_run, color);
        }

        // Diamond centered on the waypoint, so the leg that ends there meets the middle of the marker.
        private void DrawDiamond(CelestialBody body, Camera camera, double latitude, double longitude, Color color)
        {
            Vector3d radial = body.GetWorldSurfacePosition(latitude, longitude, RouteAltitudeM) - body.position;
            Vector2 center;
            if (!TryProject(body, camera, body.position + radial, out center))
            {
                return;
            }

            Vector2 north = center + new Vector2(0f, -DiamondPx);
            Vector2 east = center + new Vector2(DiamondPx, 0f);
            Vector2 south = center + new Vector2(0f, DiamondPx);
            Vector2 west = center + new Vector2(-DiamondPx, 0f);
            DrawScreenLine(north, east, color, LineWidthPx);
            DrawScreenLine(east, south, color, LineWidthPx);
            DrawScreenLine(south, west, color, LineWidthPx);
            DrawScreenLine(west, north, color, LineWidthPx);
        }

        private void DrawLabels(CelestialBody body, Camera camera, NavPlan plan)
        {
            EnsureLabel();
            for (int i = 0; i < plan.Count; i++)
            {
                Vector3d radial = body.GetWorldSurfacePosition(plan[i].Latitude, plan[i].Longitude, RouteAltitudeM) - body.position;
                Vector2 screen;
                if (!TryProject(body, camera, body.position + radial, out screen))
                {
                    continue;
                }

                Color color = PointColor(plan, i);
                Rect rect = new Rect(screen.x + 14f, screen.y - 8f, 240f, 32f);
                string text = NavPlan.NameOf(i) + "\n" + Format.LatLon(plan[i].Latitude, plan[i].Longitude);
                _label.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _label);
                _label.normal.textColor = color;
                GUI.Label(rect, text, _label);
            }
        }

        private static void Stroke(List<Vector2> points, Color color)
        {
            for (int i = 1; i < points.Count; i++)
            {
                DrawScreenLine(points[i - 1], points[i], color, LineWidthPx);
            }
        }

        private static void DrawScreenLine(Vector2 a, Vector2 b, Color color, float width)
        {
            if (Texture2D.whiteTexture == null)
            {
                return;
            }

            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.5f)
            {
                return;
            }

            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            GUI.color = color;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - (width * 0.5f), length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        private static bool TryProject(CelestialBody body, Camera camera, Vector3d world, out Vector2 gui)
        {
            Vector3 scaled = Scaled(world);
            Vector3 screen = camera.WorldToScreenPoint(scaled);
            gui = new Vector2(screen.x, Screen.height - screen.y);
            if (screen.z <= 0f)
            {
                return false;
            }

            Vector3 bodyScaled = Scaled(body.position);
            return Vector3.Dot(scaled - bodyScaled, camera.transform.position - bodyScaled) > 0f;
        }

        private static Vector3d SlerpRadial(Vector3d a, Vector3d b, double t)
        {
            double magA = a.magnitude;
            double magB = b.magnitude;
            if (magA < 1.0 || magB < 1.0)
            {
                return a;
            }

            Vector3d na = a / magA;
            Vector3d nb = b / magB;
            double dot = (na.x * nb.x) + (na.y * nb.y) + (na.z * nb.z);
            if (dot > 1.0)
            {
                dot = 1.0;
            }
            else if (dot < -1.0)
            {
                dot = -1.0;
            }

            double theta = Math.Acos(dot);
            double radius = magA + ((magB - magA) * t);
            if (theta < 1e-4)
            {
                Vector3d blended = na + ((nb - na) * t);
                double mag = blended.magnitude;
                return mag < 1e-8 ? na * radius : blended * (radius / mag);
            }

            double sinTheta = Math.Sin(theta);
            double w1 = Math.Sin((1.0 - t) * theta) / sinTheta;
            double w2 = Math.Sin(t * theta) / sinTheta;
            return ((na * w1) + (nb * w2)) * radius;
        }

        private static Vector3 Scaled(Vector3d world)
        {
            Vector3d scaled = ScaledSpace.LocalToScaledSpace(world);
            return new Vector3((float)scaled.x, (float)scaled.y, (float)scaled.z);
        }

        private static Color PointColor(NavPlan plan, int index)
        {
            if (index == plan.ActiveIndex)
            {
                return Active;
            }

            return index < plan.ActiveIndex ? Past : FuturePoint;
        }

        private void EnsureLabel()
        {
            if (_label != null)
            {
                return;
            }

            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 12;
            _label.alignment = TextAnchor.UpperLeft;
        }
    }
}
