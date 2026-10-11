using UnityEngine;

namespace AutopilotCore
{
    public sealed class AltitudeHold : IAutopilotModule
    {
        private readonly Pid _altitudePid = new Pid(0.02f, 0.0002f, 0.04f, 200f);
        private readonly PitchHold _pitchHold = new PitchHold();
        private float _target = 1000f;

        public string Name
        {
            get { return "Altitude Hold"; }
        }

        public bool Enabled { get; set; }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _altitudePid.Reset();
                _pitchHold.Enabled = false;
                _pitchHold.Update(vessel, ctrl);
                return;
            }

            float error = _target - vessel.GetAltitudeASL();
            float pitchTarget = _altitudePid.Step(error, TimeWarp.fixedDeltaTime);
            pitchTarget = Mathf.Clamp(pitchTarget, -25f, 25f);

            _pitchHold.Enabled = true;
            _pitchHold.Apply(vessel, ctrl, pitchTarget);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            GUILayout.Label("Alvo ASL (m): " + _target.ToString("F0"));
            _target = GUILayout.HorizontalSlider(_target, 0f, 25000f);
        }
    }
}
