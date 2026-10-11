using System.Collections;
using UnityEngine;

namespace AutopilotCore
{
    public class Controller : MonoBehaviour
    {
        private Vessel _vessel;
        private ModuleManager _modules;
        private MainWindow _window;

        private void Start()
        {
            _modules = new ModuleManager();
            _window = new MainWindow(_modules);
            AttachVessel(FlightGlobals.ActiveVessel);
            GameEvents.onVesselChange.Add(OnVesselChange);
            StartCoroutine(RenderInstrumentsLoop());
        }

        // GL drawing into RenderTextures is safe after the frame is rendered; IMGUI shows it next frame.
        private IEnumerator RenderInstrumentsLoop()
        {
            WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (_window != null)
                {
                    _window.RenderInstruments(_vessel);
                }
            }
        }

        private void OnVesselChange(Vessel vessel)
        {
            AttachVessel(vessel);
        }

        private void AttachVessel(Vessel vessel)
        {
            if (_vessel != null)
            {
                _vessel.OnFlyByWire -= Fly;
            }

            _vessel = vessel;

            if (_vessel != null)
            {
                _vessel.OnFlyByWire += Fly;
            }
        }

        private void Fly(FlightCtrlState ctrl)
        {
            if (_vessel == null)
            {
                return;
            }

            _modules.UpdateAll(_vessel, ctrl);
        }

        private void OnGUI()
        {
            if (_window != null)
            {
                _window.Draw(_vessel);
            }
        }

        private void OnDestroy()
        {
            if (_vessel != null)
            {
                _vessel.OnFlyByWire -= Fly;
                _vessel = null;
            }

            GameEvents.onVesselChange.Remove(OnVesselChange);

            if (_window != null)
            {
                _window.Dispose();
                _window = null;
            }
        }
    }
}
