using UnityEngine;

namespace AutopilotCore
{
    // Rudder = pedal + washed-out yaw damper around the coordinated yaw rate + sideslip P; gains scale with 1/q.
    // No integrator: rudder and aileron trims drifted together into a cross-control sideslip could not see.
    public sealed class YawControl
    {
        private const float MinQ = 0.3f;
        private const float MaxQ = 4f;
        private const float Slew = 3f;
        private const float Damp = 2.5f;
        private const float WashoutTau = 2f;
        private const float SlipGain = 3f;
        private const float PedalHandover = 0.15f;
        private const float MinCoordSpeed = 20f;
        private const float RateTau = 0.03f;

        private readonly OscillationGuard _guard;
        private float _washout;
        private bool _coordinating;
        private float _yawRate;
        private float _rollRate;
        private float _lastTick = float.NegativeInfinity;

        public YawControl(string name)
        {
            _guard = new OscillationGuard(name, 0.02f);
        }

        public float Output { get; private set; }

        public float Gain
        {
            get { return _guard.Gain; }
        }

        public void Reset(float output)
        {
            Output = output;
            _washout = 0f;
            _guard.Reset();
        }

        // Zero lateral load: r = g sin(phi) cos(theta) / V, plus p tan(alpha) to roll about the airflow.
        public static float CoordinatedRate(Vessel vessel, float rollRate, float speed)
        {
            if (speed <= MinCoordSpeed)
            {
                return 0f;
            }

            float phi = vessel.GetBank() * Mathf.Deg2Rad;
            float theta = vessel.GetPitch() * Mathf.Deg2Rad;
            float alpha = Mathf.Clamp(vessel.GetAoA(), -20f, 20f) * Mathf.Deg2Rad;
            return (vessel.GetGravity() * Mathf.Sin(phi) * Mathf.Cos(theta) / speed) + (rollRate * Mathf.Tan(alpha));
        }

        public float Step(float pedal, float coordRate, float yawRate, float slipError, float qRatio, bool pilotActive, float dt)
        {
            float q = Mathf.Clamp(qRatio, MinQ, MaxQ);
            float gain = _guard.Gain / q;
            float handover = 1f - Mathf.Clamp01(Mathf.Abs(pedal) / PedalHandover);
            float rateError = coordRate - yawRate;
            _washout = Filter.Blend(_washout, rateError, dt, WashoutTau);
            float damper = Damp * gain * (rateError - _washout);
            float slip = handover * SlipGain * gain * slipError;
            float wanted = Mathf.Clamp(pedal + damper + slip, -1f, 1f);
            Output = Mathf.MoveTowards(Output, wanted, Slew * dt);
            _guard.Step(rateError - _washout, pilotActive, dt);
            return Output;
        }

        // Turn coordination for the autopilot modes: no pedal, zero sideslip.
        public void Coordinate(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            if (!vessel.TryGetBodyRates(out Vector3 body))
            {
                return;
            }

            bool resumed = Time.fixedTime - _lastTick > 0.5f;
            _lastTick = Time.fixedTime;
            if (!_coordinating || resumed)
            {
                Reset(ctrl.yaw);
                _yawRate = body.z;
                _rollRate = body.y;
                _coordinating = true;
            }

            _yawRate = Filter.Blend(_yawRate, body.z, dt, RateTau);
            _rollRate = Filter.Blend(_rollRate, body.y, dt, RateTau);
            float speed = (float)vessel.GetAirVelocity().magnitude;
            float coordRate = CoordinatedRate(vessel, _rollRate, speed);
            float slipError = speed > MinCoordSpeed ? vessel.GetSideslip() : 0f;
            ctrl.yaw = Step(0f, coordRate, _yawRate, slipError, vessel.GetDynamicRatio(), false, dt);
        }

        public void Release()
        {
            _coordinating = false;
        }
    }
}
