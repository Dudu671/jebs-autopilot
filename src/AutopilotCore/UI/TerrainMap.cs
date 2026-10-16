using System;
using System.Diagnostics;
using UnityEngine;

namespace AutopilotCore
{
    // Terrain map around a point in surface colors with relief shading, sampled incrementally so
    // map rebuilds never stall a frame. Azimuthal equidistant around the center, so it holds up at the poles.
    // Terrain height is the expensive sample, so a rebuild copies every pixel of the last map that was
    // sampled at the same point, and samples coarse to fine so a new area shows early.
    public sealed class TerrainMap
    {
        private const int Size = 256;
        private const int Total = Size * Size;
        private const float RecenterFraction = 0.3f;
        private const float Relief = 3f;
        private const float Dim = 0.8f;

        // A pixel of the last map is reused when it was sampled this close (cells) to the new pixel's point.
        // The check is against where it was sampled, so copies of copies never drift further. The two
        // projections part with distance from the center, so wide maps resample their edges.
        private const float ReuseTolerance = 0.35f;

        // Sampling passes, coarse to fine; a preview is shown after each pass from this stride on.
        private const int FirstPreviewStride = 4;
        private static readonly int[] Strides = { 16, 8, 4, 2, 1 };
        private static readonly int[] PassEnds = new int[Strides.Length];
        private static readonly int[] Order = BuildOrder();

        private readonly Color32[] _pixels = new Color32[Total];
        private readonly SurfaceColors _surface = new SurfaceColors();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private Layer _building = new Layer();
        private Layer _last = new Layer();
        private Texture2D _texture;

        private bool _isBuilding;
        private bool _preview;
        private int _next;
        private int _pass;

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
            get { return _isBuilding; }
        }

        public float Progress
        {
            get { return _isBuilding ? _next / (float)Total : 1f; }
        }

        public void Update(CelestialBody body, double lat, double lon, float halfSizeM, float budgetMs)
        {
            if (body == null)
            {
                return;
            }

            if (NeedsNewMap(body, lat, lon, halfSizeM))
            {
                // A preview only helps where the map shown does not cover the new one: a new body, or zooming out.
                _preview = !HasMap || Body != body || halfSizeM > HalfSizeM + 1f;
                _isBuilding = true;
                _next = 0;
                _pass = 0;
                SnapToLast(body, halfSizeM, ref lat, ref lon);
                _building.Start(body, lat, lon, halfSizeM, _surface.Prepare(body));
            }

            if (_isBuilding)
            {
                Step(budgetMs);
            }
        }

        // East/north offset in meters of (lat, lon) from the displayed map center.
        public Vector2 OffsetMeters(double lat, double lon)
        {
            return Body == null ? Vector2.zero : NavMath.LocalOffset(Body.Radius, CenterLat, CenterLon, lat, lon);
        }

        public void Dispose()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            _surface.Dispose();
            _last.Valid = false;
            HasMap = false;
        }

        private bool NeedsNewMap(CelestialBody body, double lat, double lon, float halfSizeM)
        {
            if (_isBuilding)
            {
                // Let a build finish after small moves; it is recentered on the next check.
                return _building.Body != body || Mathf.Abs(_building.HalfSize - halfSizeM) > 1f;
            }

            if (!HasMap || Body != body || Mathf.Abs(HalfSizeM - halfSizeM) > 1f)
            {
                return true;
            }

            Vector2 offset = OffsetMeters(lat, lon);
            return Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > HalfSizeM * RecenterFraction;
        }

        private void Step(float budgetMs)
        {
            _stopwatch.Reset();
            _stopwatch.Start();

            Layer map = _building;
            double radius = map.Body.Radius;
            bool hasTerrain = map.Body.pqsController != null;
            bool reuse = _last.Valid && _last.Body == map.Body && Mathf.Abs(_last.HalfSize - map.HalfSize) < 1f &&
                         _last.HasColors == map.HasColors;

            while (_next < Total)
            {
                int idx = Order[_next];
                double east = (-1.0 + (((idx % Size) + 0.5) * 2.0 / Size)) * map.HalfSize;
                double north = (-1.0 + (((idx / Size) + 0.5) * 2.0 / Size)) * map.HalfSize;
                double lat;
                double lon;
                NavMath.FromLocal(radius, map.CenterLat, map.CenterLon, east, north, out lat, out lon);
                if (!reuse || !Reuse(idx, radius, lat, lon))
                {
                    map.Heights[idx] = hasTerrain ? (float)SampleHeight(map.Body, lat, lon) : 0f;
                    map.Colors[idx] = map.HasColors ? _surface.Sample(lat, lon) : default(Color32);
                    map.Lats[idx] = lat;
                    map.Lons[idx] = lon;
                }

                _next++;
                if (_next == PassEnds[_pass])
                {
                    if (_preview && Strides[_pass] <= FirstPreviewStride && _next < Total)
                    {
                        Render(Strides[_pass]);
                    }

                    _pass++;
                }

                if ((_next & 31) == 0 && _stopwatch.Elapsed.TotalMilliseconds > budgetMs)
                {
                    return;
                }
            }

            Render(1);
            _building.Valid = true;
            Layer finished = _building;
            _building = _last;
            _last = finished;
            _isBuilding = false;
        }

        // A recentered map is centered on a pixel of the last one, so its pixels fall on the old points
        // instead of between them; the center stays within half a pixel of the requested point.
        private void SnapToLast(CelestialBody body, float halfSizeM, ref double lat, ref double lon)
        {
            if (!_last.Valid || _last.Body != body || Mathf.Abs(_last.HalfSize - halfSizeM) >= 1f)
            {
                return;
            }

            float cell = 2f * _last.HalfSize / Size;
            Vector2 offset = NavMath.LocalOffset(body.Radius, _last.CenterLat, _last.CenterLon, lat, lon);
            double east = Mathf.Round(offset.x / cell) * cell;
            double north = Mathf.Round(offset.y / cell) * cell;
            NavMath.FromLocal(body.Radius, _last.CenterLat, _last.CenterLon, east, north, out lat, out lon);
        }

        // Copies the pixel of the last map sampled at about this point, if there is one.
        private bool Reuse(int idx, double radius, double lat, double lon)
        {
            Layer last = _last;
            float cell = 2f * last.HalfSize / Size;
            Vector2 offset = NavMath.LocalOffset(radius, last.CenterLat, last.CenterLon, lat, lon);
            int i = Mathf.RoundToInt((offset.x / cell) + (Size * 0.5f) - 0.5f);
            int j = Mathf.RoundToInt((offset.y / cell) + (Size * 0.5f) - 0.5f);
            if (i < 0 || j < 0 || i >= Size || j >= Size)
            {
                return false;
            }

            int k = (j * Size) + i;
            if (NavMath.Distance(radius, last.Lats[k], last.Lons[k], lat, lon) > ReuseTolerance * cell)
            {
                return false;
            }

            Layer map = _building;
            map.Heights[idx] = last.Heights[k];
            map.Colors[idx] = last.Colors[k];
            map.Lats[idx] = last.Lats[k];
            map.Lons[idx] = last.Lons[k];
            return true;
        }

        // Draws the map from the points sampled every `step` pixels, each filling its block, and shows it.
        private void Render(int step)
        {
            Layer map = _building;
            bool ocean = map.Body.ocean;
            float cell = 2f * map.HalfSize / Size;
            for (int j = 0; j < Size; j += step)
            {
                for (int i = 0; i < Size; i += step)
                {
                    int idx = (j * Size) + i;
                    float h = map.Heights[idx];
                    bool water = ocean && h <= 0f;
                    float shade = water ? 1f : Shade(i, j, step, cell);
                    Color32 color = map.HasColors
                        ? Tint(map.Colors[idx], shade * Dim)
                        : Fallback(h, water, ocean && IsCoast(i, j, step), shade);
                    Fill(i, j, step, color);
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

            Body = map.Body;
            CenterLat = map.CenterLat;
            CenterLon = map.CenterLon;
            HalfSizeM = map.HalfSize;
            HasMap = true;
        }

        private void Fill(int i, int j, int step, Color32 color)
        {
            int jEnd = Mathf.Min(j + step, Size);
            int iEnd = Mathf.Min(i + step, Size);
            for (int y = j; y < jEnd; y++)
            {
                int row = y * Size;
                for (int x = i; x < iEnd; x++)
                {
                    _pixels[row + x] = color;
                }
            }
        }

        // Lambert shading with light from the northwest, relative to flat ground (1 = flat), relief exaggerated.
        private float Shade(int i, int j, int step, float cell)
        {
            float span = 2f * step * cell;
            float dzdx = (Height(i + step, j, step) - Height(i - step, j, step)) / span * Relief;
            float dzdy = (Height(i, j + step, step) - Height(i, j - step, step)) / span * Relief;
            Vector3 normal = new Vector3(-dzdx, -dzdy, 1f).normalized;
            Vector3 light = new Vector3(-1f, 1f, 1.4f).normalized;
            return Mathf.Clamp(Vector3.Dot(normal, light) / light.z, 0.45f, 1.5f);
        }

        // Height at the nearest sampled point inside the map.
        private float Height(int i, int j, int step)
        {
            int last = (Size - 1) - ((Size - 1) % step);
            i = Mathf.Clamp(i, 0, last);
            j = Mathf.Clamp(j, 0, last);
            return Mathf.Max(0f, _building.Heights[(j * Size) + i]);
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

        private bool IsCoast(int i, int j, int step)
        {
            return IsWater(i - step, j) || IsWater(i + step, j) || IsWater(i, j - step) || IsWater(i, j + step);
        }

        private bool IsWater(int i, int j)
        {
            if (i < 0 || j < 0 || i >= Size || j >= Size)
            {
                return false;
            }

            return _building.Heights[(j * Size) + i] <= 0f;
        }

        // Pixel indices pass by pass: every 16th pixel, then the 8th not yet taken, down to every pixel.
        private static int[] BuildOrder()
        {
            int[] order = new int[Total];
            bool[] taken = new bool[Total];
            int n = 0;
            for (int pass = 0; pass < Strides.Length; pass++)
            {
                int stride = Strides[pass];
                for (int j = 0; j < Size; j += stride)
                {
                    for (int i = 0; i < Size; i += stride)
                    {
                        int idx = (j * Size) + i;
                        if (!taken[idx])
                        {
                            taken[idx] = true;
                            order[n++] = idx;
                        }
                    }
                }

                PassEnds[pass] = n;
            }

            return order;
        }

        // One map's samples, with the point each pixel was actually sampled at, so a later map can reuse them.
        private sealed class Layer
        {
            public readonly float[] Heights = new float[Total];
            public readonly Color32[] Colors = new Color32[Total];
            public readonly double[] Lats = new double[Total];
            public readonly double[] Lons = new double[Total];
            public CelestialBody Body;
            public double CenterLat;
            public double CenterLon;
            public float HalfSize;
            public bool HasColors;
            public bool Valid;

            public void Start(CelestialBody body, double lat, double lon, float halfSize, bool hasColors)
            {
                Body = body;
                CenterLat = lat;
                CenterLon = lon;
                HalfSize = halfSize;
                HasColors = hasColors;
                Valid = false;
            }
        }
    }
}
