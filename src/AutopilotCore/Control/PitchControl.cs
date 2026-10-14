using UnityEngine;

namespace AutopilotCore
{
    // Holds a pitch attitude through the body pitch-rate loop, with turn compensation and the
    // AoA/G envelope.
    public sealed class PitchControl
    {
        private const float AttitudeGain = 1.2f;
        private const float MaxPitchRate = 0.12f;
        private const float RateTau = 0.03f;

        private readonly RateLoop _loop;
        private bool _active;
        private float _rate;
        private float _lastTick = float.NegativeInfinity;

        public PitchControl(string name)
        {
            _loop = new RateLoop(name, 0.9f, 1.6f, 1.5f, 2f, 0.03f);
        }

        // True while the envelope is cutting the commanded pitch rate.
        public bool Limited { get; private set; }

        public float RateCommand { get; private set; }

        public float Rate
        {
            get { return _rate; }
        }

        public void Reset()
        {
            _active = false;
            Limited = false;
        }

        public void Step(Vessel vessel, FlightCtrlState ctrl, float targetDeg, float dt)
        {
            if (!vessel.TryGetBodyRates(out Vector3 body))
            {
                return;
            }

            // A gap means the vessel was on rails; its state is stale.
            bool resumed = Time.fixedTime - _lastTick > 0.5f;
            _lastTick = Time.fixedTime;
            if (!_active || resumed)
            {
                _loop.Reset(ctrl.pitch);
                _rate = body.x;
                _active = true;
            }

            _rate = Filter.Blend(_rate, body.x, dt, RateTau);

            float pitch = vessel.GetPitch();
            float bank = vessel.GetBank();
            float speed = (float)vessel.GetAirVelocity().magnitude;
            float cosPhi = Mathf.Cos(bank * Mathf.Deg2Rad);
            float error = Mathf.DeltaAngle(pitch, targetDeg) * Mathf.Deg2Rad;
            float thetaDot = Mathf.Clamp(AttitudeGain * error, -MaxPitchRate, MaxPitchRate);

            // Elevator no longer moves the pitch angle at steep bank.
            float command = Mathf.Abs(cosPhi) > 0.3f ? thetaDot * cosPhi : 0f;
            command += Kinematics.TurnPitchRate(bank, pitch, speed, vessel.GetGravity());

            RateCommand = Envelope.Limit(command, vessel.GetAoA(), speed, vessel.GetGravity(), pitch, bank,
                true, Envelope.DefaultMaxLoad, Envelope.DefaultMinLoad);
            Limited = Mathf.Abs(RateCommand - command) > 1e-4f;
            ctrl.pitch = _loop.Step(RateCommand, _rate, vessel.GetDynamicRatio(), 0f, false, dt);
        }
    }
}
