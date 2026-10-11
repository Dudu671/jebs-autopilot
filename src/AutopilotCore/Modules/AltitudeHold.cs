using System;
using UnityEngine;

namespace AutopilotCore
{
    public sealed class AltitudeHold : IAutopilotModule
    {
        private const float CaptureGain = 2f;
        private const float VsCmdAccelFpmPerSec = 250f;
        private const float PitchTargetRate = 3f;
        private const float Kp = 1.0f;
        private const float Ki = 0.2f;
        private const float Kd = 0.6f;
        private const float MinPitch = -15f;
        private const float MaxPitch = 20f;

        private readonly PitchHold _pitchHold = new PitchHold();
        private readonly FloatField _targetFt = new FloatField(3000f);
        private readonly FloatField _vsLimitFpm = new FloatField(1000f);
        private readonly FlightRecorder _recorder = new FlightRecorder(
            "altitude_hold.csv",
            "t,alt_ft,vs_fpm,vs_cmd_fpm,fpa,fpa_cmd,fpa_rate,pitch,pitch_target,stick,gain");

        private bool _active;
        private float _vsCmd;
        private float _trimPitch;
        private float _lastFpa;
        private float _fpaRate;
        private float _pitchTarget;
        private float _gainScale = 1f;

        private float _time;
        private int _lastErrSign;
        private float _lastCrossTime;
        private float _peakErr;
        private int _fastCrossings;
        private float _sameSignTime;

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
                _pitchHold.Reset();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float pitch = vessel.GetPitch();
            float vs = vessel.GetVerticalSpeedFpm();
            float groundSpeed = Mathf.Max(vessel.GetGroundVelocity().magnitude, 30f);
            float fpa = FlightPathAngle(vs, groundSpeed);

            if (!_active)
            {
                Engage(pitch, vs, fpa);
            }

            float limitFpm = Mathf.Max(1f, Mathf.Abs(_vsLimitFpm.Value));
            float vsWanted = Mathf.Clamp(CaptureGain * (_targetFt.Value - vessel.GetAltitudeAslFt()), -limitFpm, limitFpm);
            _vsCmd = Mathf.MoveTowards(_vsCmd, vsWanted, VsCmdAccelFpmPerSec * dt);

            _fpaRate = Filter.Blend(_fpaRate, (fpa - _lastFpa) / dt, dt, 0.4f);
            _lastFpa = fpa;

            float fpaCmd = FlightPathAngle(_vsCmd, groundSpeed);
            float fpaError = fpaCmd - fpa;
            Adapt(fpaError, dt);

            float g = _gainScale;
            // Integrate only while the pitch loop can follow, so the trim does not wind up.
            bool tracking = Mathf.Abs(_pitchTarget - pitch) < 4f;
            if (tracking)
            {
                _trimPitch = Mathf.Clamp(_trimPitch + (g * Ki * fpaError * dt), MinPitch, MaxPitch);
            }

            float wanted = _trimPitch + (g * ((Kp * fpaError) - (Kd * _fpaRate)));
            wanted = Mathf.Clamp(wanted, MinPitch, MaxPitch);
            _pitchTarget = Mathf.MoveTowards(_pitchTarget, wanted, PitchTargetRate * dt);

            _pitchHold.Apply(vessel, ctrl, _pitchTarget);

            if (_recorder.Tick(dt))
            {
                _recorder.Record(vessel.GetAltitudeAslFt(), vs, _vsCmd, fpa, fpaCmd, _fpaRate,
                    pitch, _pitchTarget, ctrl.pitch, _gainScale);
            }
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            GUILayout.BeginHorizontal();
            _targetFt.Draw("ALT (ft)", 58f);
            _vsLimitFpm.Draw("VS limit (fpm)", 88f);
            GUILayout.EndHorizontal();
            if (_active)
            {
                GUILayout.Label("VS cmd " + _vsCmd.ToString("F0") + " fpm   pitch tgt " +
                                _pitchTarget.ToString("F1") + "   gain " + _gainScale.ToString("F2"));
            }
        }

        private void Engage(float pitch, float vs, float fpa)
        {
            _active = true;
            _vsCmd = vs;
            _trimPitch = pitch;
            _lastFpa = fpa;
            _fpaRate = 0f;
            _pitchTarget = pitch;
            _time = 0f;
            _lastErrSign = 0;
            _lastCrossTime = 0f;
            _peakErr = 0f;
            _fastCrossings = 0;
            _sameSignTime = 0f;
            _pitchHold.Reset();
            _recorder.Start();
        }

        private void Adapt(float fpaError, float dt)
        {
            const float Hysteresis = 0.15f;
            _time += dt;
            _peakErr = Mathf.Max(_peakErr, Mathf.Abs(fpaError));

            int sign = fpaError > Hysteresis ? 1 : (fpaError < -Hysteresis ? -1 : 0);
            if (sign == 0)
            {
                _sameSignTime = 0f;
                return;
            }

            if (_lastErrSign != 0 && sign != _lastErrSign)
            {
                // Two quick, sizeable reversals in a row mean the loop is oscillating.
                bool fast = (_time - _lastCrossTime) < 20f && _peakErr > 0.4f;
                _fastCrossings = fast ? _fastCrossings + 1 : 0;
                if (_fastCrossings >= 2)
                {
                    _gainScale = Mathf.Max(0.3f, _gainScale * 0.85f);
                    _fastCrossings = 0;
                }

                _lastCrossTime = _time;
                _peakErr = 0f;
                _sameSignTime = 0f;
            }
            else
            {
                _sameSignTime += dt;
                if (_sameSignTime > 15f && Mathf.Abs(fpaError) > 0.3f)
                {
                    _gainScale = Mathf.Min(2f, _gainScale * 1.05f);
                    _sameSignTime = 10f;
                }
            }

            _lastErrSign = sign;
        }

        private static float FlightPathAngle(float vsFpm, float tasMps)
        {
            float vsMps = vsFpm / Units.FpmPerMps;
            float sin = Mathf.Clamp(vsMps / tasMps, -1f, 1f);
            return (float)(Math.Asin(sin) * (180.0 / Math.PI));
        }
    }
}
