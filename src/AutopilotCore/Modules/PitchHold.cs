using UnityEngine;

namespace AutopilotCore
{
    public sealed class PitchHold : IAutopilotModule
    {
        private readonly Pid _pid = new Pid(0.04f, 0.002f, 0.03f, 8f);
        private float _target;

        public string Name
        {
            get { return "Pitch Hold"; }
        }

        public bool Enabled { get; set; }

        public float Target
        {
            get { return _target; }
            set { _target = value; }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _pid.Reset();
                return;
            }

            Apply(vessel, ctrl, _target);
        }

        public void Apply(Vessel vessel, FlightCtrlState ctrl, float pitchTarget)
        {
            float error = pitchTarget - vessel.GetPitch();
            float output = _pid.Step(error, TimeWarp.fixedDeltaTime);
            ctrl.pitch = Mathf.Clamp(output, -1f, 1f);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            GUILayout.Label("Alvo (graus): " + _target.ToString("F1"));
            _target = GUILayout.HorizontalSlider(_target, -90f, 90f);
        }
    }
}
