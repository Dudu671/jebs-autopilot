using System;
using UnityEngine;

namespace AutopilotCore
{
    // Surface color of a body, read from its scaled-space (orbital view) texture. The texture is
    // copied once into a readable one, and the lat/lon -> UV mapping is fitted from the scaled
    // mesh's own vertices, so no projection convention is assumed.
    public sealed class SurfaceColors : IDisposable
    {
        private const int MaxWidth = 2048;
        private const int MaxSamples = 600;

        private CelestialBody _body;
        private Texture2D _texture;
        private bool _valid;
        private float _u0;
        private float _uSign;
        private float _v0;
        private float _vSign;

        public bool Prepare(CelestialBody body)
        {
            if (body == _body)
            {
                return _valid;
            }

            Dispose();
            _body = body;
            try
            {
                _valid = body != null && Calibrate(body) && Copy(body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutopilotCore] Surface colors unavailable for " + body.bodyName + ": " + ex.Message);
                _valid = false;
            }

            return _valid;
        }

        public Color Sample(double latitude, double longitude)
        {
            float u = Mathf.Repeat(_u0 + (_uSign * (float)(longitude / 360.0)), 1f);
            float v = Mathf.Clamp01(_v0 + (_vSign * (float)(latitude / 180.0)));
            return _texture.GetPixelBilinear(u, v);
        }

        public void Dispose()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            _body = null;
            _valid = false;
        }

        private bool Calibrate(CelestialBody body)
        {
            MeshFilter filter = body.scaledBody != null ? body.scaledBody.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                return false;
            }

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            if (vertices.Length == 0 || uvs.Length != vertices.Length)
            {
                return false;
            }

            Transform transform = body.scaledBody.transform;
            int step = Math.Max(1, vertices.Length / MaxSamples);
            int count = (vertices.Length + step - 1) / step;
            float[] lats = new float[count];
            float[] lons = new float[count];
            float[] us = new float[count];
            float[] vs = new float[count];
            int n = 0;
            for (int i = 0; i < vertices.Length; i += step)
            {
                Vector3d world = ScaledSpace.ScaledToLocalSpace(transform.TransformPoint(vertices[i]));
                lats[n] = (float)body.GetLatitude(world);
                lons[n] = (float)body.GetLongitude(world);
                us[n] = uvs[i].x;
                vs[n] = uvs[i].y;
                n++;
            }

            // u = u0 + s * lon / 360 (wrapping): pick the sign whose offsets agree best on the circle.
            float bestSpread = -1f;
            for (int s = -1; s <= 1; s += 2)
            {
                double sumSin = 0.0;
                double sumCos = 0.0;
                int used = 0;
                for (int i = 0; i < n; i++)
                {
                    if (Mathf.Abs(lats[i]) > 60f)
                    {
                        continue;
                    }

                    double offset = (us[i] - (s * lons[i] / 360.0)) * 2.0 * Math.PI;
                    sumSin += Math.Sin(offset);
                    sumCos += Math.Cos(offset);
                    used++;
                }

                if (used == 0)
                {
                    return false;
                }

                float agreement = (float)(Math.Sqrt((sumSin * sumSin) + (sumCos * sumCos)) / used);
                if (agreement > bestSpread)
                {
                    bestSpread = agreement;
                    _uSign = s;
                    _u0 = Mathf.Repeat((float)(Math.Atan2(sumSin, sumCos) / (2.0 * Math.PI)), 1f);
                }
            }

            // v = v0 + t * lat / 180: pick the sign with the smaller spread.
            float bestVariance = float.MaxValue;
            for (int t = -1; t <= 1; t += 2)
            {
                double sum = 0.0;
                double sumSq = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double offset = vs[i] - (t * lats[i] / 180.0);
                    sum += offset;
                    sumSq += offset * offset;
                }

                double mean = sum / n;
                float variance = (float)((sumSq / n) - (mean * mean));
                if (variance < bestVariance)
                {
                    bestVariance = variance;
                    _vSign = t;
                    _v0 = (float)mean;
                }
            }

            return bestSpread > 0.95f && bestVariance < 0.001f;
        }

        private bool Copy(CelestialBody body)
        {
            Renderer renderer = body.scaledBody.GetComponent<Renderer>();
            Texture source = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                return false;
            }

            int width = Math.Min(source.width, MaxWidth);
            int height = Math.Max(1, width * source.height / source.width);
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                _texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                _texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                _texture.Apply(false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            return true;
        }
    }
}
