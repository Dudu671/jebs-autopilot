using UnityEngine;

namespace AutopilotCore
{
    public sealed class MainWindow
    {
        private const int WindowId = 0x4A454253; // "JEBS"

        private readonly ModuleManager _modules;
        private readonly FlightTape _tape = new FlightTape();
        private readonly FlyByWire _flyByWire = new FlyByWire();
        private readonly FlightData _data = new FlightData();
        private readonly PrimaryFlightDisplay _pfd = new PrimaryFlightDisplay();
        private readonly NavigationDisplay _nav;
        private readonly MapRouteOverlay _mapRoute;
        private readonly DirectionalGyroWindow _gyro = new DirectionalGyroWindow();
        private readonly InstrumentWindow[] _instruments;
        private Rect _rect = new Rect(40f, 80f, 380f, 0f);
        private Vessel _vessel;

        public MainWindow(ModuleManager modules)
        {
            _modules = modules;
            _nav = new NavigationDisplay(modules.Find<NavHold>());
            _mapRoute = new MapRouteOverlay(modules.Find<NavHold>());
            _instruments = new InstrumentWindow[] { _pfd, _nav, _gyro };
        }

        public void Draw(Vessel vessel)
        {
            _vessel = vessel;
            // Shrinking only on Layout keeps the size stable across mouse events, which caused flicker.
            if (Event.current.type == EventType.Layout)
            {
                _rect.height = 0f;
            }

            _rect = GUILayout.Window(WindowId, _rect, DrawContents, WindowTitle(), GUILayout.Width(380f));

            for (int i = 0; i < _instruments.Length; i++)
            {
                _instruments[i].DrawWindow();
            }

            _mapRoute.Sync(vessel);
        }

        public void ApplyFlyByWire(Vessel vessel, FlightCtrlState ctrl)
        {
            PitchHold pitch = _modules.Find<PitchHold>();
            AltitudeHold altitude = _modules.Find<AltitudeHold>();
            HeadingHold heading = _modules.Find<HeadingHold>();
            NavHold nav = _modules.Find<NavHold>();
            bool pitchOwned = (pitch != null && pitch.Enabled) || (altitude != null && altitude.Enabled);
            bool rollOwned = (heading != null && heading.Enabled) || (nav != null && nav.Enabled);
            _flyByWire.Apply(vessel, ctrl, pitchOwned, rollOwned);
        }

        public void FinishFlyByWire(FlightCtrlState ctrl)
        {
            _flyByWire.ReassertYaw(ctrl);
        }

        public void PollWaypointPick()
        {
            _nav.PollMapClick(_vessel, MouseOverWindow);
        }

        private bool MouseOverWindow(Vector2 guiMouse)
        {
            if (_rect.height > 1f && _rect.Contains(guiMouse))
            {
                return true;
            }

            for (int i = 0; i < _instruments.Length; i++)
            {
                InstrumentWindow window = _instruments[i];
                if (window.Visible && window.WindowRect.height > 1f && window.WindowRect.Contains(guiMouse))
                {
                    return true;
                }
            }

            return false;
        }

        public void RenderInstruments(Vessel vessel)
        {
            bool anyVisible = false;
            for (int i = 0; i < _instruments.Length; i++)
            {
                anyVisible |= _instruments[i].Visible;
            }

            if (!anyVisible)
            {
                return;
            }

            _data.Update(vessel, _modules, Time.deltaTime);
            for (int i = 0; i < _instruments.Length; i++)
            {
                _instruments[i].RenderFrame(_data);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _instruments.Length; i++)
            {
                _instruments[i].Dispose();
            }

            _mapRoute.Dispose();
        }

        private static string WindowTitle()
        {
            System.Version version = typeof(MainWindow).Assembly.GetName().Version;
            return "Jeb's Autopilot  " + version;
        }

        private void DrawContents(int id)
        {
            _tape.Draw(_vessel);

            GUILayout.Space(6f);
            _flyByWire.Enabled = GUILayout.Toggle(_flyByWire.Enabled, "Fly-by-wire", GUI.skin.button);

            GUILayout.Space(6f);
            _modules.DrawAllGui();

            GUILayout.Space(8f);
            if (GUILayout.Button("Disable all"))
            {
                _modules.DisableAll();
            }

            GUILayout.Space(10f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Instruments");
            GUILayout.BeginHorizontal();
            _pfd.Visible = GUILayout.Toggle(_pfd.Visible, "PFD", GUI.skin.button);
            _nav.Visible = GUILayout.Toggle(_nav.Visible, "Nav display", GUI.skin.button);
            _gyro.Visible = GUILayout.Toggle(_gyro.Visible, "Directional gyro", GUI.skin.button);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }
}
