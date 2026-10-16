using UnityEngine;

namespace AutopilotCore
{
    // Throttle = trim + P on speed error - D on IAS acceleration. The trim integrates in throttle
    // units, so it carries the steady throttle the target speed needs.
    public sealed class ThrottleControl
    {
        // Per kt, per kt*s and per kt/s; tuned in a model with 0.3-5 s engine spool.
        private const float Kp = 0.06f;
        private const float Ki = 0.012f;
        private const float Kd = 0.15f;
        private const float AccelTau = 1f;

        private bool _active;
        private float _trim;
        private float _lastIas;
        private float _accel;

        public void Reset()
        {
            _active = false;
        }

        public float Step(float ias, float targetKt, float throttle, float dt)
        {
            if (!_active)
            {
                _active = true;
                _trim = throttle;
                _lastIas = ias;
                _accel = 0f;
            }

            _accel = Filter.Blend(_accel, (ias - _lastIas) / dt, dt, AccelTau);
            _lastIas = ias;

            float error = targetKt - ias;
            float unclamped = _trim + (Kp * error) - (Kd * _accel);
            bool saturated = (unclamped >= 1f && error > 0f) || (unclamped <= 0f && error < 0f);
            if (!saturated)
            {
                _trim = Mathf.Clamp01(_trim + (Ki * error * dt));
            }

            return Mathf.Clamp01(unclamped);
        }
    }
}
