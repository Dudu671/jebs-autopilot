using System;
using System.Diagnostics;
using UnityEngine;

namespace AutopilotCore
{
    // Terrain map around a point in surface colors with relief shading, sampled incrementally so
    // map rebuilds never stall a frame. Azimuthal equidistant around the center, so it holds up at the poles.
    public sealed class TerrainMap
    {
        private const int Size = 256;
        private const float RecenterFraction = 0.3f;
        private const float Relief = 3f;
        private const float Dim = 0.8f;

        private readonly float[] _heights = new float[Size * Size];
        private readonly Color32[] _colors = new Color32[Size * Size];
        private readonly Color32[] _pixels = new Color32[Size * Size];
        private readonly SurfaceColors _surface = new SurfaceColors();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private Texture2D _texture;

        private bool _building;
        private int _next;
        private CelestialBody _buildBody;
        private bool _hasColors;
        private double _buildLat;
        private double _buildLon;
        private float _buildHalf;

        public bool HasMap { get; private set; }
        public CelestialBody Body { get; private set; }
        public double CenterLat { get; private set; }
        public double CenterLon { get; private set; }
        public float HalfSizeM { get; private set; }

        public Texture2D Texture
        {
            get { return _texture; }
        }

        public bool Building
        {
            get { return _building; }
        }

        public float Progress
        {
            get { return _building ? _next / (float)(Size * Size) : 1f; }
        }

        public void Update(CelestialBody body, double lat, double lon, float halfSizeM, float budgetMs)
        {
            if (body == null)
            {
                return;
            }

            if (NeedsNewMap(body, lat, lon, halfSizeM))
            {
                _building = true;
                _next = 0;
                _buildBody = body;
                _buildLat = lat;
                _buildLon = lon;
                _buildHalf = halfSizeM;
                _hasColors = _surface.Prepare(body);
            }

            if (_building)
            {
                Step(budgetMs);
            }
        }

        // East/north offset in meters of (lat, lon) from the displayed map center.
        public Vector2 OffsetMeters(double lat, double lon)
        {
            return Offset(Body, CenterLat, CenterLon, lat, lon);
        }

        public void Dispose()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            _surface.Dispose();
            HasMap = false;
        }

        private bool NeedsNewMap(CelestialBody body, double lat, double lon, float halfSizeM)
        {
            if (_building)
            {
                // Let a build finish after small moves; it is recentered on the next check.
                return _buildBody != body || Mathf.Abs(_buildHalf - halfSizeM) > 1f;
            }

            if (!HasMap || Body != body || Mathf.Abs(HalfSizeM - halfSizeM) > 1f)
            {
                return true;
            }

            Vector2 offset = OffsetMeters(lat, lon);
            return Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > HalfSizeM * RecenterFraction;
        }

        private static Vector2 Offset(CelestialBody body, double lat0, double lon0, double lat, double lon)
        {
            return body == null ? Vector2.zero : NavMath.LocalOffset(body.Radius, lat0, lon0, lat, lon);
        }

        private void Step(float budgetMs)
        {
            _stopwatch.Reset();
            _stopwatch.Start();

            int total = Size * Size;
            double radius = _buildBody.Radius;
            bool hasTerrain = _buildBody.pqsController != null;

            while (_next < total)
            {
                int i = _next % Size;
                int j = _next / Size;
                double east = (-1.0 + ((i + 0.5) * 2.0 / Size)) * _buildHalf;
                double north = (-1.0 + ((j + 0.5) * 2.0 / Size)) * _buildHalf;
                double lat;
                double lon;
                NavMath.FromLocal(radius, _buildLat, _buildLon, east, north, out lat, out lon);
                _heights[_next] = hasTerrain ? (float)SampleHeight(_buildBody, lat, lon) : 0f;
                if (_hasColors)
                {
                    _colors[_next] = _surface.Sample(lat, lon);
                }

                _next++;

                if ((_next & 31) == 0 && _stopwatch.Elapsed.TotalMilliseconds > budgetMs)
                {
                    return;
                }
            }

            Finish();
        }

        private void Finish()
        {
            bool ocean = _buildBody.ocean;
            float cell = 2f * _buildHalf / Size;
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int idx = (j * Size) + i;
                    float h = _heights[idx];
                    bool water = ocean && h <= 0f;
                    float shade = water ? 1f : Shade(i, j, cell);
                    _pixels[idx] = _hasColors ? Tint(_colors[idx], shade * Dim) : Fallback(h, water, ocean && IsCoast(i, j), shade);
                }
            }

            if (_texture == null)
            {
                _texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
                _texture.filterMode = FilterMode.Trilinear;
                _texture.wrapMode = TextureWrapMode.Clamp;
                _texture.anisoLevel = 2;
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(true);

            Body = _buildBody;
            CenterLat = _buildLat;
            CenterLon = _buildLon;
            HalfSizeM = _buildHalf;
            HasMap = true;
            _building = false;
        }

        // Lambert shading with light from the northwest, relative to flat ground (1 = flat), relief exaggerated.
        private float Shade(int i, int j, float cell)
        {
            float dzdx = (Height(i + 1, j) - Height(i - 1, j)) / (2f * cell) * Relief;
            float dzdy = (Height(i, j + 1) - Height(i, j - 1)) / (2f * cell) * Relief;
            Vector3 normal = new Vector3(-dzdx, -dzdy, 1f).normalized;
            Vector3 light = new Vector3(-1f, 1f, 1.4f).normalized;
            return Mathf.Clamp(Vector3.Dot(normal, light) / light.z, 0.45f, 1.5f);
        }

        private float Height(int i, int j)
        {
            i = Mathf.Clamp(i, 0, Size - 1);
            j = Mathf.Clamp(j, 0, Size - 1);
            return Mathf.Max(0f, _heights[(j * Size) + i]);
        }

        private static Color32 Tint(Color32 color, float k)
        {
            return new Color32(Scale(color.r, k), Scale(color.g, k), Scale(color.b, k), 255);
        }

        private static byte Scale(byte channel, float k)
        {
            return (byte)Mathf.Clamp(channel * k, 0f, 255f);
        }

        // Green phosphor palette, for bodies whose surface texture cannot be read.
        private static Color32 Fallback(float h, bool water, bool coast, float shade)
        {
            if (water)
            {
                return new Color32(0, 24, 12, 255);
            }

            if (coast)
            {
                return new Color32(120, 255, 150, 255);
            }

            float k = (0.2f + (0.55f * Mathf.Sqrt(Mathf.Clamp01(h / 4000f)))) * shade;
            return new Color32(Scale(60, k), Scale(255, k), Scale(100, k), 255);
        }

        // GetSurfaceHeight still answers in map view, when TerrainAltitude can collapse to sea level.
        private static double SampleHeight(CelestialBody body, double latitude, double longitude)
        {
            PQS pqs = body.pqsController;
            if (pqs != null)
            {
                Vector3d radial = body.GetRelSurfaceNVector(latitude, longitude);
                double surface = pqs.GetSurfaceHeight(radial);
                if (surface > 1.0)
                {
                    return surface - body.Radius;
                }
            }

            return body.TerrainAltitude(latitude, longitude, true);
        }

        private bool IsCoast(int i, int j)
        {
            return IsWater(i - 1, j) || IsWater(i + 1, j) || IsWater(i, j - 1) || IsWater(i, j + 1);
        }

        private bool IsWater(int i, int j)
        {
            if (i < 0 || j < 0 || i >= Size || j >= Size)
            {
                return false;
            }

            return _heights[(j * Size) + i] <= 0f;
        }
    }
}
