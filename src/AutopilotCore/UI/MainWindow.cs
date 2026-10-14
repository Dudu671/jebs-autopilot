using UnityEngine;

namespace AutopilotCore
{
    // Always shows and edits the active vessel's autopilot.
    public sealed class MainWindow
    {
        private const int WindowId = 0x4A454253; // "JEBS"

        private readonly FlightTape _tape = new FlightTape();
        private readonly FlightData _data = new FlightData();
        private readonly PrimaryFlightDisplay _pfd = new PrimaryFlightDisplay();
        private readonly NavigationDisplay _nav = new NavigationDisplay();
        private readonly MapRouteOverlay _mapRoute = new MapRouteOverlay();
        private readonly DirectionalGyroWindow _gyro = new DirectionalGyroWindow();
        private readonly InstrumentWindow[] _instruments;
        private Rect _rect = new Rect(40f, 80f, 380f, 0f);
        private VesselPilot _pilot;

        public MainWindow()
        {
            _instruments = new InstrumentWindow[] { _pfd, _nav, _gyro };
        }

        public void Draw(VesselPilot pilot)
        {
            Bind(pilot);
            if (_pilot == null)
            {
                return;
            }

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

            _mapRoute.Sync(_pilot.Vessel, _pilot.Nav);
        }

        public void PollWaypointPick()
        {
            if (_pilot != null)
            {
                _nav.PollMapClick(_pilot.Vessel, MouseOverWindow);
            }
        }

        private void Bind(VesselPilot pilot)
        {
            if (pilot == _pilot)
            {
                return;
            }

            _pilot = pilot;
            _nav.Bind(pilot != null ? pilot.Nav : null);
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

        public void RenderInstruments(VesselPilot pilot)
        {
            bool anyVisible = false;
            for (int i = 0; i < _instruments.Length; i++)
            {
                anyVisible |= _instruments[i].Visible;
            }

            if (!anyVisible || pilot == null)
            {
                return;
            }

            Bind(pilot);
            _data.Update(pilot.Vessel, pilot.Modules, Time.deltaTime);
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
            _tape.Draw(_pilot.Vessel);

            GUILayout.Space(6f);
            _pilot.FlyByWire.Enabled = GUILayout.Toggle(_pilot.FlyByWire.Enabled, "Fly-by-wire", GUI.skin.button);

            GUILayout.Space(6f);
            _pilot.Modules.DrawAllGui();

            GUILayout.Space(8f);
            if (GUILayout.Button("Disable all"))
            {
                _pilot.Modules.DisableAll();
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
