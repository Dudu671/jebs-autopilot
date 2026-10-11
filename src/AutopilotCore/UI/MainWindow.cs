using UnityEngine;

namespace AutopilotCore
{
    public sealed class MainWindow
    {
        private const int WindowId = 0x4A454253; // "JEBS"

        private readonly ModuleManager _modules;
        private readonly FlightTape _tape = new FlightTape();
        private Rect _rect = new Rect(40f, 80f, 320f, 520f);
        private Vessel _vessel;

        public MainWindow(ModuleManager modules)
        {
            _modules = modules;
        }

        public void Draw(Vessel vessel)
        {
            _vessel = vessel;
            _rect = GUI.Window(WindowId, _rect, DrawContents, WindowTitle());
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
            _modules.DrawAllGui();

            GUILayout.Space(8f);
            if (GUILayout.Button("Disable all"))
            {
                _modules.DisableAll();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }
    }
}
