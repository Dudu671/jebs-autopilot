using UnityEngine;

namespace AutopilotCore
{
    // Rudder and nose wheel onto a runway centerline. A steady side force, such as asymmetric drag or
    // wind, needs steady rudder; the proportional terms fade with dynamic pressure, so integrals carry it.
    public sealed class RunwaySteering
    {
        // Steer at a point ahead on the centerline; it moves out with speed.
        private const float Lookahead = 60f;
        private const float LookaheadTime = 3f;
        private const float MaxOffset = 5f;
        private const float Gain = 3f;
        private const float Damp = 1.5f;
        private const float WheelSteerSpeed = 8f;

        // Heading correction (deg) per meter of cross track per second, and its limit.
        private const float CrossTrackIntegral = 0.02f;
        private const float MaxBias = 3f;

        // Rudder per radian of heading error per second, scaled down with dynamic pressure like the
        // proportional term, so the integral time stays about Gain / RudderIntegral seconds.
        private const float RudderIntegral = 1f;
        private const float MaxTrim = 0.5f;

        private float _bias;
        private float _trim;

        public void Reset()
        {
            _bias = 0f;
            _trim = 0f;
        }

        // Rudder that turns the nose onto a heading.
        public float RudderToHeading(Vessel vessel, float heading, float dt)
        {
            float error = Mathf.DeltaAngle(vessel.GetHeading(), heading) * Mathf.Deg2Rad;
            Vector3 body;
            float yawRate = vessel.TryGetBodyRates(out body) ? body.z : 0f;
            float q = Mathf.Max(vessel.GetDynamicRatio(), 1f);
            float proportional = ((Gain * error) - (Damp * yawRate)) / q;
            float unclamped = proportional + _trim;
            bool saturated = (unclamped >= 1f && error > 0f) || (unclamped <= -1f && error < 0f);
            if (!saturated)
            {
                _trim = Mathf.Clamp(_trim + (RudderIntegral * error / q * dt), -MaxTrim, MaxTrim);
            }

            return Mathf.Clamp(proportional + _trim, -1f, 1f);
        }

        // Cross: meters right of the centerline. Returns the track steered at.
        public float Steer(Vessel vessel, FlightCtrlState ctrl, float course, float cross, float dt)
        {
            float groundSpeed = (float)vessel.horizontalSrfSpeed;
            float lookahead = Mathf.Max(Lookahead, LookaheadTime * groundSpeed);
            _bias = Mathf.Clamp(_bias - (CrossTrackIntegral * cross * dt), -MaxBias, MaxBias);
            float offset = Mathf.Clamp(-Mathf.Atan(cross / lookahead) * Mathf.Rad2Deg, -MaxOffset, MaxOffset) + _bias;
            float track = Mathf.Repeat(course + offset, 360f);
            float steer = RudderToHeading(vessel, track, dt);
            ctrl.yaw = steer;

            // wheelSteer has the opposite sign of yaw.
            ctrl.wheelSteer = -steer * Mathf.Clamp(WheelSteerSpeed / Mathf.Max(groundSpeed, 1f), 0.1f, 1f);
            return track;
        }
    }
}
