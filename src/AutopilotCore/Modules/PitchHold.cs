using UnityEngine;

namespace AutopilotCore
{
    public sealed class PitchHold : IVerticalMode
    {
        private readonly PitchControl _control = new PitchControl("Pitch hold");
        private float _target;

        public string Name
        {
            get { return "Pitch Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetPitch
        {
            get { return _target; }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _control.Reset();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            _control.Step(vessel, ctrl, _target, dt);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            GUILayout.Label("Target (deg): " + _target.ToString("F1") + (_control.Limited ? "   AOA/G LIMIT" : ""));
            _target = GUILayout.HorizontalSlider(_target, -90f, 90f);
        }
    }
}
