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
        private bool _collapsed;
        private bool _mouseOverGui;
        private ModuleGroup[] _groups;

        public MainWindow()
        {
            _instruments = new InstrumentWindow[] { _pfd, _nav, _gyro };
        }

        // Toggled from the toolbar; the instrument windows keep their own switches.
        public bool Visible { get; set; }

        public void Draw(VesselPilot pilot)
        {
            Bind(pilot);
            if (_pilot == null)
            {
                return;
            }

            if (Visible)
            {
                // Shrinking only on Layout keeps the size stable across mouse events, which caused flicker.
                if (Event.current.type == EventType.Layout)
                {
                    _rect.height = 0f;
                }

                _rect = GUILayout.Window(WindowId, _rect, DrawContents, "Jeb's Autopilot", GUILayout.Width(380f));
            }

            for (int i = 0; i < _instruments.Length; i++)
            {
                _instruments[i].DrawWindow();
            }

            _mapRoute.Sync(_pilot.Vessel, _pilot.Nav);

            // Tested in GUI space against the final window rects; Update has neither.
            if (Event.current.type == EventType.Repaint)
            {
                _mouseOverGui = MouseOverWindow(Event.current.mousePosition);
            }
        }

        public void PollWaypointPick()
        {
            if (_pilot != null)
            {
                _nav.PollMapClick(_pilot.Vessel, _mouseOverGui);
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
            _groups = pilot != null ? BuildGroups(pilot.Modules) : null;
        }

        private static ModuleGroup[] BuildGroups(ModuleManager modules)
        {
            return new[]
            {
                new ModuleGroup("Lateral", new[] { "HDG", "NAV", "HOLD" },
                    modules.Find<HeadingHold>(), modules.Find<NavHold>(), modules.Find<HoldPattern>()),
                new ModuleGroup("Vertical", new[] { "ALT", "PITCH" }, modules.Find<AltitudeHold>(), modules.Find<PitchHold>()),
                new ModuleGroup("Speed", null, modules.Find<SpeedHold>()),
                new ModuleGroup("Approach", null, modules.Find<Autoland>()),
            };
        }

        private bool MouseOverWindow(Vector2 guiMouse)
        {
            if (Visible && _rect.height > 1f && _rect.Contains(guiMouse))
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

        private void DrawContents(int id)
        {
            if (GUI.Button(new Rect(_rect.width - 26f, 3f, 20f, 16f), _collapsed ? "+" : "-"))
            {
                _collapsed = !_collapsed;
            }

            _tape.Draw(_pilot.Vessel);
            if (_collapsed)
            {
                GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
                return;
            }

            GUILayout.BeginHorizontal();
            _pfd.Visible = GUILayout.Toggle(_pfd.Visible, "PFD", GuiStyles.ToggleButton);
            _nav.Visible = GUILayout.Toggle(_nav.Visible, "Nav display", GuiStyles.ToggleButton);
            _gyro.Visible = GUILayout.Toggle(_gyro.Visible, "Directional gyro", GuiStyles.ToggleButton);
            GUILayout.EndHorizontal();

            Divider.Draw();
            FlyByWire flyByWire = _pilot.FlyByWire;
            if (Foldout.Header("Fly-by-wire", flyByWire.Summary, flyByWire.Enabled))
            {
                flyByWire.DrawGui();
                GUILayout.Space(6f);
            }

            for (int i = 0; i < _groups.Length; i++)
            {
                _groups[i].Draw(_pilot.Modules);
            }

            Divider.Draw();
            if (GUILayout.Button("Disable all"))
            {
                _pilot.Modules.DisableAll();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }
}
