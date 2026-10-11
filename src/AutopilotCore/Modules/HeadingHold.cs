using UnityEngine;

namespace AutopilotCore
{
    public sealed class HeadingHold : IAutopilotModule
    {
        private readonly Pid _pid = new Pid(0.03f, 0.001f, 0.04f, 8f);
        private float _target;

        public string Name
        {
            get { return "Heading Hold"; }
        }

        public bool Enabled { get; set; }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _pid.Reset();
                return;
            }

            float error = Mathf.DeltaAngle(vessel.GetHeading(), _target);
            float output = _pid.Step(error, TimeWarp.fixedDeltaTime);
            ctrl.yaw = Mathf.Clamp(output, -1f, 1f);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            GUILayout.Label("Alvo (graus): " + _target.ToString("F0"));
            _target = GUILayout.HorizontalSlider(_target, 0f, 360f);
        }
    }
}
