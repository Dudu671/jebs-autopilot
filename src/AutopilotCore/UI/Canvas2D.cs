using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AutopilotCore
{
    // Draws into a RenderTexture with GL so instruments can rotate and clip without IMGUI's pixel snapping.
    public sealed class Canvas2D
    {
        private struct Vertex
        {
            public float X;
            public float Y;
            public float U;
            public float V;

            public Vertex(float x, float y, float u, float v)
            {
                X = x;
                Y = y;
                U = u;
                V = v;
            }
        }

        private const int MaxVertices = 128;

        private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");
        private static readonly int GuiZTestId = Shader.PropertyToID("unity_GUIZTestMode");

        private readonly int _width;
        private readonly int _height;
        private readonly Vertex[] _input = new Vertex[MaxVertices];
        private readonly Vertex[] _bufferA = new Vertex[MaxVertices];
        private readonly Vertex[] _bufferB = new Vertex[MaxVertices];
        private readonly Stack<Rect> _clipStack = new Stack<Rect>();

        private RenderTexture _target;
        private Material _colorMaterial;
        private Material _textureMaterial;
        private Font _font;
        private Font _fallbackFont;
        private bool _builtinFontTried;

        private RenderTexture _previousTarget;
        private Vector4 _previousClip;
        private float _previousGuiZTest;
        private Material _activeMaterial;
        private Texture _activeTexture;
        private bool _batchOpen;
        private Rect _clip;

        public Canvas2D(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        public Texture Texture
        {
            get { return _target; }
        }

        public void SetFallbackFont(Font font)
        {
            if (font != null)
            {
                _fallbackFont = font;
            }
        }

        public bool Begin(Color clear)
        {
            if (!EnsureResources())
            {
                return false;
            }

            _previousTarget = RenderTexture.active;
            RenderTexture.active = _target;
            // Map view leaves UI clip and depth-test state that discards this texture.
            _previousClip = Shader.GetGlobalVector(ClipRectId);
            _previousGuiZTest = Shader.GetGlobalFloat(GuiZTestId);
            ApplyGuiDrawState();
            GL.Viewport(new Rect(0f, 0f, _width, _height));
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, _width, _height, 0f);
            GL.Clear(false, true, clear);
            _clipStack.Clear();
            _clip = new Rect(0f, 0f, _width, _height);
            return true;
        }

        public void End()
        {
            Flush();
            GL.PopMatrix();
            Shader.SetGlobalVector(ClipRectId, _previousClip);
            Shader.SetGlobalFloat(GuiZTestId, _previousGuiZTest);
            RenderTexture.active = _previousTarget;
            _previousTarget = null;
            _activeMaterial = null;
            _activeTexture = null;
        }

        public void Dispose()
        {
            if (_target != null)
            {
                _target.Release();
                Object.Destroy(_target);
                _target = null;
            }

            DestroyMaterial(ref _colorMaterial);
            DestroyMaterial(ref _textureMaterial);
        }

        public void PushClip(Rect rect)
        {
            _clipStack.Push(_clip);
            float xMin = Mathf.Max(_clip.xMin, rect.xMin);
            float yMin = Mathf.Max(_clip.yMin, rect.yMin);
            float xMax = Mathf.Min(_clip.xMax, rect.xMax);
            float yMax = Mathf.Min(_clip.yMax, rect.yMax);
            _clip = Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        public void PopClip()
        {
            _clip = _clipStack.Pop();
        }

        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2((v.x * cos) - (v.y * sin), (v.x * sin) + (v.y * cos));
        }

        // Compass-style angle: 0 = up, clockwise positive.
        public static Vector2 Polar(Vector2 center, float radius, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(center.x + (radius * Mathf.Sin(rad)), center.y - (radius * Mathf.Cos(rad)));
        }

        public void FillRect(Rect r, Color color)
        {
            FillQuad(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), color);
        }

        public void StrokeRect(Rect r, float width, Color color)
        {
            FillRect(new Rect(r.xMin, r.yMin, r.width, width), color);
            FillRect(new Rect(r.xMin, r.yMax - width, r.width, width), color);
            FillRect(new Rect(r.xMin, r.yMin, width, r.height), color);
            FillRect(new Rect(r.xMax - width, r.yMin, width, r.height), color);
        }

        public void FillQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            _input[0] = new Vertex(a.x, a.y, 0f, 0f);
            _input[1] = new Vertex(b.x, b.y, 0f, 0f);
            _input[2] = new Vertex(c.x, c.y, 0f, 0f);
            _input[3] = new Vertex(d.x, d.y, 0f, 0f);
            Emit(_colorMaterial, null, 4, color);
        }

        public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            _input[0] = new Vertex(a.x, a.y, 0f, 0f);
            _input[1] = new Vertex(b.x, b.y, 0f, 0f);
            _input[2] = new Vertex(c.x, c.y, 0f, 0f);
            Emit(_colorMaterial, null, 3, color);
        }

        public void Line(Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            FillQuad(a + n, b + n, b - n, a - n, color);
        }

        public void DashedLine(Vector2 a, Vector2 b, float width, float dash, float gap, Color color)
        {
            float length = Vector2.Distance(a, b);
            if (length < 1e-3f)
            {
                return;
            }

            Vector2 dir = (b - a) / length;
            for (float s = 0f; s < length; s += dash + gap)
            {
                Line(a + (dir * s), a + (dir * Mathf.Min(s + dash, length)), width, color);
            }
        }

        public void Arc(Vector2 center, float radius, float fromDeg, float toDeg, float width, Color color)
        {
            int segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 4f));
            float inner = radius - (width * 0.5f);
            float outer = radius + (width * 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Lerp(fromDeg, toDeg, i / (float)segments);
                float a1 = Mathf.Lerp(fromDeg, toDeg, (i + 1) / (float)segments);
                FillQuad(Polar(center, outer, a0), Polar(center, outer, a1), Polar(center, inner, a1), Polar(center, inner, a0), color);
            }
        }

        public void Circle(Vector2 center, float radius, float width, Color color)
        {
            Arc(center, radius, 0f, 360f, width, color);
        }

        public void FillCircle(Vector2 center, float radius, Color color)
        {
            const int segments = 36;
            for (int i = 0; i < segments; i++)
            {
                Vector2 p = Polar(center, radius, i * (360f / segments));
                _input[i] = new Vertex(p.x, p.y, 0f, 0f);
            }

            Emit(_colorMaterial, null, segments, color);
        }

        public void DrawTexture(Texture texture, Vector2 center, Vector2 size, float angleDeg, Color tint)
        {
            if (texture == null)
            {
                return;
            }

            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            Vector2 tl = center + Rotate(new Vector2(-hx, -hy), angleDeg);
            Vector2 tr = center + Rotate(new Vector2(hx, -hy), angleDeg);
            Vector2 br = center + Rotate(new Vector2(hx, hy), angleDeg);
            Vector2 bl = center + Rotate(new Vector2(-hx, hy), angleDeg);
            _input[0] = new Vertex(tl.x, tl.y, 0f, 1f);
            _input[1] = new Vertex(tr.x, tr.y, 1f, 1f);
            _input[2] = new Vertex(br.x, br.y, 1f, 0f);
            _input[3] = new Vertex(bl.x, bl.y, 0f, 0f);
            Emit(_textureMaterial, texture, 4, tint);
        }

        public float TextWidth(string text, int size)
        {
            Font font = ActiveFont();
            if (font == null || string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            font.RequestCharactersInTexture(text, size);
            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                CharacterInfo info;
                if (font.GetCharacterInfo(text[i], out info, size))
                {
                    width += info.advance;
                }
            }

            return width;
        }

        // y is the vertical center of capital letters and digits.
        public void Text(string text, float x, float y, int size, Color color, TextAlign align)
        {
            Font font = ActiveFont();
            if (font == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            float width = TextWidth(text, size);
            float pen = align == TextAlign.Left ? x : (align == TextAlign.Center ? x - (width * 0.5f) : x - width);
            pen = Mathf.Round(pen);
            float baseline = Mathf.Round(y + (size * 0.36f));

            for (int i = 0; i < text.Length; i++)
            {
                CharacterInfo info;
                if (!font.GetCharacterInfo(text[i], out info, size))
                {
                    continue;
                }

                float left = pen + info.minX;
                float right = pen + info.maxX;
                float top = baseline - info.maxY;
                float bottom = baseline - info.minY;
                _input[0] = new Vertex(left, top, info.uvTopLeft.x, info.uvTopLeft.y);
                _input[1] = new Vertex(right, top, info.uvTopRight.x, info.uvTopRight.y);
                _input[2] = new Vertex(right, bottom, info.uvBottomRight.x, info.uvBottomRight.y);
                _input[3] = new Vertex(left, bottom, info.uvBottomLeft.x, info.uvBottomLeft.y);
                Emit(font.material, null, 4, color);
                pen += info.advance;
            }
        }

        private bool EnsureResources()
        {
            if (_colorMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    return false;
                }

                _colorMaterial = new Material(shader);
                _colorMaterial.hideFlags = HideFlags.HideAndDontSave;
                _colorMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _colorMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _colorMaterial.SetInt("_Cull", (int)CullMode.Off);
                _colorMaterial.SetInt("_ZWrite", 0);
                _colorMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            }

            if (_textureMaterial == null)
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader == null)
                {
                    return false;
                }

                _textureMaterial = new Material(shader);
                _textureMaterial.hideFlags = HideFlags.HideAndDontSave;
            }

            if (_target == null || !_target.IsCreated())
            {
                if (_target != null)
                {
                    Object.Destroy(_target);
                }

                _target = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32);
                _target.antiAliasing = 4;
                _target.filterMode = FilterMode.Bilinear;
                _target.Create();
            }

            return true;
        }

        private Font ActiveFont()
        {
            if (_font == null && !_builtinFontTried)
            {
                _builtinFontTried = true;
                try
                {
                    _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch (System.Exception)
                {
                    _font = null;
                }
            }

            return _font != null ? _font : _fallbackFont;
        }

        private void Emit(Material material, Texture texture, int count, Color color)
        {
            if (material == null)
            {
                return;
            }

            Vertex[] poly = _input;
            if (!FullyInside(count))
            {
                poly = ClipToRect(ref count);
                if (count < 3)
                {
                    return;
                }
            }

            Use(material, texture);
            GL.Color(color);
            for (int i = 1; i < count - 1; i++)
            {
                Put(poly[0]);
                Put(poly[i]);
                Put(poly[i + 1]);
            }
        }

        private static void Put(Vertex v)
        {
            GL.TexCoord2(v.U, v.V);
            GL.Vertex3(v.X, v.Y, 0f);
        }

        private void Use(Material material, Texture texture)
        {
            if (_batchOpen && material == _activeMaterial && texture == _activeTexture)
            {
                return;
            }

            Flush();
            if (texture != null)
            {
                material.mainTexture = texture;
            }

            material.SetPass(0);
            // SetPass uploads the material clip. Put the instrument rect back afterwards.
            ApplyGuiDrawState();
            GL.Begin(GL.TRIANGLES);
            _batchOpen = true;
            _activeMaterial = material;
            _activeTexture = texture;
        }

        private static void ApplyGuiDrawState()
        {
            Shader.SetGlobalVector(ClipRectId, new Vector4(-1e8f, -1e8f, 1e8f, 1e8f));
            Shader.SetGlobalFloat(GuiZTestId, (float)CompareFunction.Always);
        }

        private void Flush()
        {
            if (_batchOpen)
            {
                GL.End();
                _batchOpen = false;
            }
        }

        private bool FullyInside(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (_input[i].X < _clip.xMin || _input[i].X > _clip.xMax || _input[i].Y < _clip.yMin || _input[i].Y > _clip.yMax)
                {
                    return false;
                }
            }

            return true;
        }

        private Vertex[] ClipToRect(ref int count)
        {
            Vertex[] src = _input;
            Vertex[] dst = _bufferA;
            for (int edge = 0; edge < 4; edge++)
            {
                count = ClipEdge(src, count, dst, edge, EdgeValue(edge));
                if (count < 3)
                {
                    return dst;
                }

                src = dst;
                dst = src == _bufferA ? _bufferB : _bufferA;
            }

            return src;
        }

        private float EdgeValue(int edge)
        {
            switch (edge)
            {
                case 0: return _clip.xMin;
                case 1: return _clip.xMax;
                case 2: return _clip.yMin;
                default: return _clip.yMax;
            }
        }

        private static int ClipEdge(Vertex[] src, int count, Vertex[] dst, int edge, float value)
        {
            int written = 0;
            if (count == 0)
            {
                return 0;
            }

            Vertex prev = src[count - 1];
            bool prevInside = InsideEdge(prev, edge, value);
            for (int i = 0; i < count && written < MaxVertices - 1; i++)
            {
                Vertex cur = src[i];
                bool curInside = InsideEdge(cur, edge, value);
                if (curInside != prevInside)
                {
                    dst[written++] = Intersect(prev, cur, edge, value);
                }

                if (curInside)
                {
                    dst[written++] = cur;
                }

                prev = cur;
                prevInside = curInside;
            }

            return written;
        }

        private static bool InsideEdge(Vertex v, int edge, float value)
        {
            switch (edge)
            {
                case 0: return v.X >= value;
                case 1: return v.X <= value;
                case 2: return v.Y >= value;
                default: return v.Y <= value;
            }
        }

        private static Vertex Intersect(Vertex a, Vertex b, int edge, float value)
        {
            float t = edge < 2 ? (value - a.X) / (b.X - a.X) : (value - a.Y) / (b.Y - a.Y);
            return new Vertex(
                a.X + ((b.X - a.X) * t),
                a.Y + ((b.Y - a.Y) * t),
                a.U + ((b.U - a.U) * t),
                a.V + ((b.V - a.V) * t));
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material != null)
            {
                Object.Destroy(material);
                material = null;
            }
        }
    }
}
