using UnityEngine;

namespace AutopilotCore
{
    // Throttle = trim + P on speed error - D on IAS acceleration. The trim integrates in throttle
    // units, so it carries the steady throttle the target speed needs.
    public sealed class SpeedHold : IAutopilotModule
    {
        // Per kt, per kt*s and per kt/s; tuned in a model with 0.3-5 s engine spool.
        private const float Kp = 0.06f;
        private const float Ki = 0.012f;
        private const float Kd = 0.15f;
        private const float AccelTau = 1f;

        private readonly FloatField _target = new FloatField(120f);
        private bool _active;
        private float _trim;
        private float _lastIas;
        private float _accel;

        public string Name
        {
            get { return "Speed Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetKt
        {
            get { return _target.Value; }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _active = false;
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float ias = vessel.GetIasKt();
            if (!_active)
            {
                _active = true;
                _trim = ctrl.mainThrottle;
                _lastIas = ias;
                _accel = 0f;
            }

            _accel = Filter.Blend(_accel, (ias - _lastIas) / dt, dt, AccelTau);
            _lastIas = ias;

            float error = _target.Value - ias;
            float unclamped = _trim + (Kp * error) - (Kd * _accel);
            bool saturated = (unclamped >= 1f && error > 0f) || (unclamped <= 0f && error < 0f);
            if (!saturated)
            {
                _trim = Mathf.Clamp01(_trim + (Ki * error * dt));
            }

            ctrl.mainThrottle = Mathf.Clamp01(unclamped);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            _target.Draw("Target (kt)");
        }
    }
}
