using UnityEngine;

namespace AutopilotCore
{
    public abstract class InstrumentWindow
    {
        private readonly int _windowId;
        private readonly string _title;
        private readonly Canvas2D _canvas;
        private Rect _rect;

        protected InstrumentWindow(int windowId, string title, int width, int height, float x, float y)
        {
            _windowId = windowId;
            _title = title;
            _canvas = new Canvas2D(width, height);
            _rect = new Rect(x, y, width + 16f, 0f);
        }

        public bool Visible { get; set; }

        protected virtual Color Background
        {
            get { return new Color(0.05f, 0.05f, 0.06f, 1f); }
        }

        public void RenderFrame(FlightData data)
        {
            if (!Visible || !data.Valid || !_canvas.Begin(Background))
            {
                return;
            }

            try
            {
                Render(_canvas, data);
            }
            finally
            {
                _canvas.End();
            }
        }

        public void DrawWindow()
        {
            if (!Visible)
            {
                return;
            }

            _canvas.SetFallbackFont(GUI.skin.font);
            if (Event.current.type == EventType.Layout)
            {
                _rect.height = 0f;
            }

            _rect = GUILayout.Window(_windowId, _rect, DrawContents, _title, GUILayout.Width(_canvas.Width + 16f));
        }

        public void Dispose()
        {
            _canvas.Dispose();
            OnDispose();
        }

        protected abstract void Render(Canvas2D canvas, FlightData data);

        protected virtual void DrawControls()
        {
        }

        protected virtual void OnDispose()
        {
        }

        // point is in canvas pixels, origin top-left. Return true to consume the click.
        protected virtual bool OnCanvasClick(Vector2 point, int button)
        {
            return false;
        }

        private void DrawContents(int id)
        {
            Rect r = GUILayoutUtility.GetRect(_canvas.Width, _canvas.Height, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            if (Event.current.type == EventType.Repaint && _canvas.Texture != null)
            {
                GUI.DrawTexture(r, _canvas.Texture, ScaleMode.StretchToFill, false);
            }

            Event e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition) && OnCanvasClick(e.mousePosition - r.position, e.button))
            {
                e.Use();
            }

            DrawControls();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }
}
