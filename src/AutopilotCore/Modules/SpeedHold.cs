using UnityEngine;

namespace AutopilotCore
{
    public sealed class SpeedHold : IThrottleMode
    {
        private readonly ThrottleControl _throttle = new ThrottleControl();
        private readonly FloatField _target = new FloatField(120f);

        public string Name
        {
            get { return "Speed Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetKt
        {
            get { return _target.Value; }
            set { _target.Value = value; }
        }

        public string Summary
        {
            get { return "SPD " + _target.Value.ToString("F0") + " kt"; }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _throttle.Reset();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            ctrl.mainThrottle = _throttle.Step(vessel.GetIasKt(), _target.Value, ctrl.mainThrottle, dt);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            _target.Draw("Target (kt)");
        }
    }
}
