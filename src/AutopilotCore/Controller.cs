using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AutopilotCore
{
    public class Controller : MonoBehaviour
    {
        private const float SweepInterval = 0.5f;

        private readonly Dictionary<Guid, VesselPilot> _pilots = new Dictionary<Guid, VesselPilot>();
        private readonly List<Guid> _stale = new List<Guid>();
        private MainWindow _window;
        private ToolbarButton _toolbar;
        private float _nextSweep;

        private void Start()
        {
            _window = new MainWindow();
            _toolbar = new ToolbarButton(visible => _window.Visible = visible);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            Sweep();
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
                    _window.RenderInstruments(ActivePilot());
                }
            }
        }

        private void OnVesselChange(Vessel vessel)
        {
            PilotOf(vessel);
        }

        private void OnVesselWillDestroy(Vessel vessel)
        {
            VesselPilot pilot;
            if (vessel != null && _pilots.TryGetValue(vessel.id, out pilot))
            {
                pilot.Detach();
                _pilots.Remove(vessel.id);
            }
        }

        // Event coverage for vessels appearing (load, undock, decouple, launch) is patchy, so new
        // vessels are picked up by polling.
        private void Sweep()
        {
            List<Vessel> vessels = FlightGlobals.Vessels;
            for (int i = 0; i < vessels.Count; i++)
            {
                Vessel vessel = vessels[i];
                PhysicsRange.Extend(vessel);
                if (vessel != null && vessel.loaded)
                {
                    PilotOf(vessel);
                }
            }

            _stale.Clear();
            foreach (KeyValuePair<Guid, VesselPilot> entry in _pilots)
            {
                Vessel vessel = entry.Value.Vessel;
                if (vessel == null || vessel.id != entry.Key)
                {
                    _stale.Add(entry.Key);
                }
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                _pilots[_stale[i]].Detach();
                _pilots.Remove(_stale[i]);
            }
        }

        private VesselPilot PilotOf(Vessel vessel)
        {
            if (vessel == null)
            {
                return null;
            }

            VesselPilot pilot;
            if (!_pilots.TryGetValue(vessel.id, out pilot))
            {
                pilot = new VesselPilot(vessel);
                _pilots.Add(vessel.id, pilot);
            }

            return pilot;
        }

        private VesselPilot ActivePilot()
        {
            return PilotOf(FlightGlobals.ActiveVessel);
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextSweep)
            {
                _nextSweep = Time.unscaledTime + SweepInterval;
                Sweep();
            }

            if (_window != null)
            {
                _window.PollWaypointPick();
            }
        }

        private void OnGUI()
        {
            if (_window != null)
            {
                _window.Draw(ActivePilot());
            }
        }

        private void OnDestroy()
        {
            foreach (VesselPilot pilot in _pilots.Values)
            {
                pilot.Detach();
            }

            _pilots.Clear();
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);

            if (_toolbar != null)
            {
                _toolbar.Dispose();
                _toolbar = null;
            }

            if (_window != null)
            {
                _window.Dispose();
                _window = null;
            }
        }
    }
}
