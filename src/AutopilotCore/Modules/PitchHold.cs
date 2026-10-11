using UnityEngine;

namespace AutopilotCore
{
    public sealed class PitchHold : IAutopilotModule
    {
        private const float DesignIasKt = 110f;
        private const float AngleToRate = 0.6f;
        private const float MaxPitchRate = 4f;
        private const float RateGain = 0.035f;
        private const float IntegralGain = 0.015f;

        private float _target;
        private bool _active;
        private float _lastPitch;
        private float _pitchRate;
        private float _integral;
        private float _stick;

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
                Reset();
                return;
            }

            Apply(vessel, ctrl, _target);
        }

        public void Reset()
        {
            _active = false;
        }

        public void Apply(Vessel vessel, FlightCtrlState ctrl, float pitchTarget)
        {
            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float pitch = vessel.GetPitch();
            if (!_active)
            {
                _active = true;
                _lastPitch = pitch;
                _pitchRate = 0f;
                _integral = 0f;
                _stick = ctrl.pitch;
            }

            _pitchRate = Filter.Blend(_pitchRate, (pitch - _lastPitch) / dt, dt, 0.15f);
            _lastPitch = pitch;

            // Damp on measured pitch rate instead of differentiating the error, which kicked on target changes.
            float pitchError = pitchTarget - pitch;
            float rateCmd = Mathf.Clamp(AngleToRate * pitchError, -MaxPitchRate, MaxPitchRate);
            float rateError = rateCmd - _pitchRate;
            _integral = Mathf.Clamp(_integral + (pitchError * dt), -20f, 20f);

            float scale = Mathf.Clamp(DesignIasKt / Mathf.Max(vessel.GetIasKt(), 45f), 0.35f, 2f);
            float stickCmd = scale * ((RateGain * rateError) + (IntegralGain * _integral));
            _stick = Filter.Blend(_stick, Mathf.Clamp(stickCmd, -1f, 1f), dt, 0.12f);
            ctrl.pitch = _stick;
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            GUILayout.Label("Target (deg): " + _target.ToString("F1"));
            _target = GUILayout.HorizontalSlider(_target, -90f, 90f);
        }
    }
}
