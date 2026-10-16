using System;
using UnityEngine;

namespace AutopilotCore
{
    // Vertical speed -> flight path angle -> pitch attitude. The trim integrates the path angle error,
    // so it carries the angle of attack the airframe needs.
    public sealed class VerticalSpeedControl
    {
        // Final capture time constant, and the vertical deceleration used to level off without overshoot.
        private const float CaptureTau = 5f;
        private const float CaptureAccel = 0.8f;

        // Gains tuned in a linear model over 30-250 m/s and slow-to-fast airframes.
        private const float GammaGain = 1.2f;
        private const float TrimGain = 0.4f;

        private const float MinSpeed = 20f;

        private readonly PitchControl _pitch;
        private bool _active;

        public VerticalSpeedControl(string name)
        {
            _pitch = new PitchControl(name);
        }

        public PitchControl Pitch
        {
            get { return _pitch; }
        }

        public float Gamma { get; private set; }

        public float GammaCommand { get; private set; }

        public float PitchCommand { get; private set; }

        public float Trim { get; private set; }

        // Vertical speed (m/s) that closes an altitude error (m) without overshoot, within the limit.
        public static float Capture(float error, float limit)
        {
            float distance = Mathf.Abs(error);
            return Mathf.Sign(error) * Mathf.Min(limit, Mathf.Min(distance / CaptureTau, Mathf.Sqrt(2f * CaptureAccel * distance)));
        }

        public void Reset()
        {
            _active = false;
            _pitch.Reset();
        }

        public void Step(Vessel vessel, FlightCtrlState ctrl, float vsCmd, float minPitch, float maxPitch, float dt)
        {
            float pitch = vessel.GetPitch();
            float vs = (float)vessel.verticalSpeed;
            float speed = Mathf.Max((float)vessel.srfSpeed, MinSpeed);
            Gamma = PathAngle(vs, speed);
            if (!_active)
            {
                _active = true;
                Trim = pitch - Gamma;
                PitchCommand = pitch;
                _pitch.Reset();
            }

            GammaCommand = PathAngle(vsCmd, speed);
            float gammaError = (vsCmd - vs) / speed * Mathf.Rad2Deg;
            float unclamped = GammaCommand + (GammaGain * gammaError) + Trim;
            PitchCommand = Mathf.Clamp(unclamped, minPitch, maxPitch);

            // Anti-windup: the trim holds while the pitch command is clipped or the envelope cuts it.
            bool clipped = (unclamped > maxPitch && gammaError > 0f) || (unclamped < minPitch && gammaError < 0f);
            if (!clipped && !_pitch.Limited)
            {
                Trim = Mathf.Clamp(Trim + (TrimGain * gammaError * dt), minPitch, maxPitch);
            }

            _pitch.Step(vessel, ctrl, PitchCommand, dt);
        }

        private static float PathAngle(float vs, float speed)
        {
            return (float)(Math.Asin(Mathf.Clamp(vs / speed, -1f, 1f)) * (180.0 / Math.PI));
        }
    }
}
