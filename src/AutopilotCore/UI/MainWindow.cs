using UnityEngine;

namespace AutopilotCore
{
    public sealed class MainWindow
    {
        private const int WindowId = 0x4A454253; // "JEBS"

        private readonly ModuleManager _modules;
        private Rect _rect = new Rect(40f, 80f, 280f, 420f);

        public MainWindow(ModuleManager modules)
        {
            _modules = modules;
        }

        public void Draw()
        {
            _rect = GUI.Window(WindowId, _rect, DrawContents, "Jeb's Autopilot");
        }

        private void DrawContents(int id)
        {
            _modules.DrawAllGui();

            GUILayout.Space(8f);
            if (GUILayout.Button("Desligar todos"))
            {
                _modules.DisableAll();
            }

            GUI.DragWindow();
        }
    }
}
