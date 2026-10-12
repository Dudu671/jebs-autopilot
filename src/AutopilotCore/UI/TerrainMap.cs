using System;
using System.Diagnostics;
using UnityEngine;

namespace AutopilotCore
{
    // Monochrome terrain map around a point, sampled incrementally so map rebuilds never stall a frame.
    public sealed class TerrainMap
    {
        private const int Size = 256;
        private const float RecenterFraction = 0.3f;

        private readonly float[] _heights = new float[Size * Size];
        private readonly Color32[] _pixels = new Color32[Size * Size];
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private Texture2D _texture;

        private bool _building;
        private int _next;
        private CelestialBody _buildBody;
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
            double cosLat = Math.Max(0.05, Math.Cos(_buildLat * Mathf.Deg2Rad));
            bool hasTerrain = _buildBody.pqsController != null;
            const double toDeg = 180.0 / Math.PI;

            while (_next < total)
            {
                int i = _next % Size;
                int j = _next / Size;
                double east = (-1.0 + ((i + 0.5) * 2.0 / Size)) * _buildHalf;
                double north = (-1.0 + ((j + 0.5) * 2.0 / Size)) * _buildHalf;
                double lat = Math.Max(-89.9, Math.Min(89.9, _buildLat + (north / radius * toDeg)));
                double lon = _buildLon + (east / (radius * cosLat) * toDeg);
                _heights[_next] = hasTerrain ? (float)SampleHeight(_buildBody, lat, lon) : 0f;
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
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int idx = (j * Size) + i;
                    float h = _heights[idx];
                    if (ocean && h <= 0f)
                    {
                        _pixels[idx] = new Color32(0, 24, 12, 255);
                        continue;
                    }

                    if (ocean && IsCoast(i, j))
                    {
                        _pixels[idx] = new Color32(120, 255, 150, 255);
                        continue;
                    }

                    float k = 0.2f + (0.55f * Mathf.Sqrt(Mathf.Clamp01(h / 4000f)));
                    _pixels[idx] = new Color32((byte)(k * 60f), (byte)(k * 255f), (byte)(k * 100f), 255);
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
