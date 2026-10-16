using UnityEngine;

namespace AutopilotCore
{
    // Altitude error -> vertical speed -> flight path angle -> pitch attitude.
    public sealed class AltitudeHold : IVerticalMode
    {
        private const float VsSlew = 0.8f;
        private const float MinPitch = -20f;
        private const float MaxPitch = 25f;

        private readonly VerticalSpeedControl _vertical = new VerticalSpeedControl("Altitude hold");
        private readonly FloatField _targetFt = new FloatField(3000f);
        private readonly FloatField _vsLimitFpm = new FloatField(1000f);
        private readonly FlightRecorder _recorder = new FlightRecorder(
            "altitude_hold.csv",
            "t,ias,aoa,alt_ft,target_ft,vs_fpm,vs_cmd_fpm,gamma,gamma_cmd,pitch,pitch_cmd,trim,q_cmd,q,stick,limited");

        private bool _active;
        private float _vsCmd;
        private float _errorFt;

        public string Name
        {
            get { return "Altitude Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetFt
        {
            get { return _targetFt.Value; }
            set { _targetFt.Value = value; }
        }

        public float VsLimitFpm
        {
            get { return _vsLimitFpm.Value; }
            set { _vsLimitFpm.Value = value; }
        }

        public string Summary
        {
            get { return "ALT " + _targetFt.Value.ToString("F0") + " ft"; }
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
                _vertical.Reset();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float vs = (float)vessel.verticalSpeed;
            if (!_active)
            {
                _active = true;
                _vsCmd = vs;
                _vertical.Reset();
                _recorder.Start(vessel);
            }

            float error = (_targetFt.Value - vessel.GetAltitudeAslFt()) / Units.FeetPerMeter;
            float limit = Mathf.Max(1f, Mathf.Abs(_vsLimitFpm.Value)) / Units.FpmPerMps;
            _vsCmd = Mathf.MoveTowards(_vsCmd, VerticalSpeedControl.Capture(error, limit), VsSlew * dt);
            _errorFt = error * Units.FeetPerMeter;

            _vertical.Step(vessel, ctrl, _vsCmd, MinPitch, MaxPitch, dt);

            if (_recorder.Tick(dt))
            {
                PitchControl pitch = _vertical.Pitch;
                _recorder.Record(vessel.GetIasKt(), vessel.GetAoA(), vessel.GetAltitudeAslFt(), _targetFt.Value,
                    vs * Units.FpmPerMps, _vsCmd * Units.FpmPerMps, _vertical.Gamma, _vertical.GammaCommand, vessel.GetPitch(),
                    _vertical.PitchCommand, _vertical.Trim, pitch.RateCommand * Mathf.Rad2Deg, pitch.Rate * Mathf.Rad2Deg,
                    ctrl.pitch, pitch.Limited ? 1f : 0f);
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
                                " fpm   pitch tgt " + _vertical.PitchCommand.ToString("F1") +
                                (_vertical.Pitch.Limited ? "   AOA/G LIMIT" : ""));
            }
        }
    }
}
