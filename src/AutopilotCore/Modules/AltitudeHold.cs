using System;
using UnityEngine;

namespace AutopilotCore
{
    // Altitude error -> vertical speed -> flight path angle -> pitch attitude.
    public sealed class AltitudeHold : IVerticalMode
    {
        // Final capture time constant, and the vertical deceleration used to level off without overshoot.
        private const float CaptureTau = 5f;
        private const float CaptureAccel = 0.8f;
        private const float VsSlew = 0.8f;

        // Gains tuned in a linear model over 30-250 m/s and slow-to-fast airframes.
        private const float GammaGain = 1.2f;
        private const float TrimGain = 0.4f;

        private const float MinPitch = -20f;
        private const float MaxPitch = 25f;
        private const float MinSpeed = 20f;

        private readonly PitchControl _pitch = new PitchControl("Altitude hold");
        private readonly FloatField _targetFt = new FloatField(3000f);
        private readonly FloatField _vsLimitFpm = new FloatField(1000f);
        private readonly FlightRecorder _recorder = new FlightRecorder(
            "altitude_hold.csv",
            "t,ias,aoa,alt_ft,target_ft,vs_fpm,vs_cmd_fpm,gamma,gamma_cmd,pitch,pitch_cmd,trim,q_cmd,q,stick,limited");

        private bool _active;
        private float _vsCmd;
        private float _trim;
        private float _pitchCmd;
        private float _errorFt;

        public string Name
        {
            get { return "Altitude Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetFt
        {
            get { return _targetFt.Value; }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                if (_active)
                {
                    _recorder.Stop();
                }

                _active = false;
                _pitch.Reset();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float pitch = vessel.GetPitch();
            float vs = (float)vessel.verticalSpeed;
            float speed = Mathf.Max((float)vessel.srfSpeed, MinSpeed);
            float gamma = PathAngle(vs, speed);
            if (!_active)
            {
                Engage(vessel, pitch, vs, gamma);
            }

            float error = (_targetFt.Value - vessel.GetAltitudeAslFt()) / Units.FeetPerMeter;
            float distance = Mathf.Abs(error);
            float limit = Mathf.Max(1f, Mathf.Abs(_vsLimitFpm.Value)) / Units.FpmPerMps;
            float wanted = Mathf.Sign(error) * Mathf.Min(limit, Mathf.Min(distance / CaptureTau, Mathf.Sqrt(2f * CaptureAccel * distance)));
            _vsCmd = Mathf.MoveTowards(_vsCmd, wanted, VsSlew * dt);
            _errorFt = error * Units.FeetPerMeter;

            float gammaCmd = PathAngle(_vsCmd, speed);
            float gammaError = (_vsCmd - vs) / speed * Mathf.Rad2Deg;
            float unclamped = gammaCmd + (GammaGain * gammaError) + _trim;
            _pitchCmd = Mathf.Clamp(unclamped, MinPitch, MaxPitch);

            // Anti-windup: the trim holds while the pitch command is clipped or the envelope cuts it.
            bool clipped = (unclamped > MaxPitch && gammaError > 0f) || (unclamped < MinPitch && gammaError < 0f);
            if (!clipped && !_pitch.Limited)
            {
                _trim = Mathf.Clamp(_trim + (TrimGain * gammaError * dt), MinPitch, MaxPitch);
            }

            _pitch.Step(vessel, ctrl, _pitchCmd, dt);

            if (_recorder.Tick(dt))
            {
                _recorder.Record(vessel.GetIasKt(), vessel.GetAoA(), vessel.GetAltitudeAslFt(), _targetFt.Value,
                    vs * Units.FpmPerMps, _vsCmd * Units.FpmPerMps, gamma, gammaCmd, pitch, _pitchCmd, _trim,
                    _pitch.RateCommand * Mathf.Rad2Deg, _pitch.Rate * Mathf.Rad2Deg, ctrl.pitch, _pitch.Limited ? 1f : 0f);
            }
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            GUILayout.BeginHorizontal();
            _targetFt.Draw("ALT (ft)", 58f);
            _vsLimitFpm.Draw("VS limit (fpm)", 88f);
            GUILayout.EndHorizontal();
            if (_active)
            {
                GUILayout.Label("Error " + _errorFt.ToString("+0;-0") + " ft   VS cmd " + (_vsCmd * Units.FpmPerMps).ToString("F0") +
                                " fpm   pitch tgt " + _pitchCmd.ToString("F1") + (_pitch.Limited ? "   AOA/G LIMIT" : ""));
            }
        }

        private void Engage(Vessel vessel, float pitch, float vs, float gamma)
        {
            _active = true;
            _vsCmd = vs;
            _trim = pitch - gamma;
            _pitchCmd = pitch;
            _pitch.Reset();
            _recorder.Start(vessel);
        }

        private static float PathAngle(float vs, float speed)
        {
            return (float)(Math.Asin(Mathf.Clamp(vs / speed, -1f, 1f)) * (180.0 / Math.PI));
        }
    }
}
