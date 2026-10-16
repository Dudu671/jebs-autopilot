using System;
using UnityEngine;

namespace AutopilotCore
{
    // Rate command, attitude hold, coordinated turns. Pedal drives the rudder; bank is held.
    public sealed class FlyByWire
    {
        private const int Pitch = 0;
        private const int Roll = 1;
        private const int Yaw = 2;

        private const float RollRate = 2.0f;
        private const float Precision = 0.33f;
        private const float Deadband = 0.04f;

        // Pilot input shaping: seconds to full stick, set from the window. Easing off is quicker.
        private const float MinSmoothing = 0.1f;
        private const float MaxSmoothing = 1.5f;
        private const float CloseRatio = 2.2f;
        private const float EaseRatio = 0.11f;

        // Slider ranges for the optional load limit, in g.
        private const float MinMaxLoad = 2f;
        private const float MaxMaxLoad = 15f;
        private const float MinMinLoad = -8f;
        private const float MaxMinLoad = 0f;

        private const float PitchHoldGain = 1.2f;
        private const float BankHoldGain = 1.5f;
        private const float PitchHoldRate = 0.17f;
        private const float BankHoldRate = 0.35f;

        private const float CaptureRate = 0.035f;
        private const float CaptureTimeout = 1.2f;
        private const float WingsLevel = 5f;
        private const float MaxHoldPitch = 70f;
        private const float MaxHoldBank = 75f;

        private const float DihedralPrior = 1f;
        private const float DihedralMin = -2f;
        private const float DihedralMax = 6f;
        private const float DihedralLearnTau = 1.5f;
        private const float DihedralTrimTau = 2f;
        private const float DihedralMinSlip = 0.026f;
        private const float DihedralSteadyRate = 0.05f;

        private const float MinCoordSpeed = 20f;
        private const float RateTau = 0.03f;

        private enum Mode
        {
            Manual,
            Capture,
            Hold,
            Off,
        }

        private readonly FlightRecorder _recorder = new FlightRecorder(
            "fbw.csv",
            "t,ias,q,aoa,gee,pitch,bank,beta,pitch_dot,bank_dot," +
            "p_in,p_shaped,p_mode,p_ref,p_cmd,p_rate,p_out,p_int,p_gain,p_eff," +
            "r_in,r_shaped,r_mode,r_ref,r_cmd,r_rate,r_out,r_int,r_gain,r_eff,r_xfeed,r_dihedral," +
            "y_in,y_shaped,y_coord,y_rate,y_out,y_gain,heading,limited");

        // Stick per rad/s at the reference dynamic pressure.
        private readonly RateLoop _pitchLoop = new RateLoop("FBW pitch", 0.9f, 1.6f, 1.5f, 2f, 0.03f);
        private readonly RateLoop _rollLoop = new RateLoop("FBW roll", 0.4f, 0.5f, 1f, 3f, 0.08f);
        private readonly YawControl _yaw = new YawControl("FBW yaw");
        private readonly float[] _input = new float[3];
        private readonly float[] _rate = new float[3];

        private Mode _pitchMode;
        private Mode _bankMode;
        private float _pitchRef;
        private float _bankRef;
        private float _pitchCapture;
        private float _bankCapture;
        private float _dihedral;
        private float _rollTrim;
        private float _lastPitch;
        private float _lastBank;
        private bool _ready;
        private float _lastTick = float.NegativeInfinity;
        private Guid _vesselId;

        private float _smoothing = 0.55f;
        private bool _loadLimited;
        private float _maxLoad = Envelope.DefaultMaxLoad;
        private float _minLoad = Envelope.DefaultMinLoad;

        public bool Enabled { get; set; }

        public string Summary
        {
            get
            {
                string text = Enabled ? "ON" : "OFF";
                return _loadLimited ? text + "  G " + _maxLoad.ToString("+0.0") + "/" + _minLoad.ToString("0.0") : text;
            }
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, "Fly-by-wire", GuiStyles.ToggleButton);
            GUILayout.Label("Stick smoothing: " + _smoothing.ToString("F2") + " s to full deflection");
            _smoothing = GUILayout.HorizontalSlider(_smoothing, MinSmoothing, MaxSmoothing);

            _loadLimited = GUILayout.Toggle(_loadLimited, "G limit", GuiStyles.Toggle);
            if (_loadLimited)
            {
                GUILayout.Label("Max positive: " + _maxLoad.ToString("+0.0") + " g");
                _maxLoad = Mathf.Round(GUILayout.HorizontalSlider(_maxLoad, MinMaxLoad, MaxMaxLoad) * 2f) / 2f;
                GUILayout.Label("Max negative: " + _minLoad.ToString("0.0") + " g");
                _minLoad = Mathf.Round(GUILayout.HorizontalSlider(_minLoad, MinMinLoad, MaxMinLoad) * 2f) / 2f;
            }
        }

        public void Apply(Vessel vessel, FlightCtrlState ctrl, bool piloted, bool pitchOwned, bool rollOwned)
        {
            if (!Enabled || vessel == null || vessel.ReferenceTransform == null || vessel.LandedOrSplashed)
            {
                Disarm();
                return;
            }

            if (!vessel.TryGetBodyRates(out Vector3 body))
            {
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);

            // A gap means the vessel was on rails; its state is stale.
            bool resumed = Time.fixedTime - _lastTick > 0.5f;
            _lastTick = Time.fixedTime;
            if (!_ready || vessel.id != _vesselId || resumed)
            {
                Engage(vessel, ctrl, body);
            }

            float precision = piloted ? PrecisionScale() : 0f;
            // ctrl holds the raw stick; KSP keeps the trim separate.
            float pitchIn = ctrl.pitch * precision;
            float rollIn = ctrl.roll * precision;
            float yawIn = ctrl.yaw * precision;
            bool pitchActive = Mathf.Abs(pitchIn) > Deadband;
            bool rollActive = Mathf.Abs(rollIn) > Deadband;
            bool pedalActive = Mathf.Abs(yawIn) > Deadband;
            _rate[Pitch] = Filter.Blend(_rate[Pitch], body.x, dt, RateTau);
            _rate[Roll] = Filter.Blend(_rate[Roll], body.y, dt, RateTau);
            _rate[Yaw] = Filter.Blend(_rate[Yaw], body.z, dt, RateTau);

            float pitchDeg = vessel.GetPitch();
            float bankDeg = vessel.GetBank();
            float headingDeg = vessel.GetHeading();
            float pitchDot = Mathf.DeltaAngle(_lastPitch, pitchDeg) / dt;
            float bankDot = Mathf.DeltaAngle(_lastBank, bankDeg) / dt;
            _lastPitch = pitchDeg;
            _lastBank = bankDeg;

            float theta = pitchDeg * Mathf.Deg2Rad;
            float phi = bankDeg * Mathf.Deg2Rad;
            float speed = (float)vessel.GetAirVelocity().magnitude;
            float gravity = vessel.GetGravity();
            float aoa = vessel.GetAoA();
            float gee = (float)vessel.geeForce;
            float beta = vessel.GetSideslip();
            float qRatio = vessel.GetDynamicRatio();
            float profile = Mathf.Clamp(Mathf.Sqrt(qRatio), 0.45f, 1.2f);
            ShapeInput(Pitch, pitchIn, dt);
            ShapeInput(Roll, rollIn, dt);
            ShapeInput(Yaw, yawIn, dt);

            bool airborne = speed > MinCoordSpeed;
            float turnRate = Kinematics.LevelTurnRate(bankDeg, speed, gravity);
            float pitchComp = Kinematics.TurnPitchRate(bankDeg, pitchDeg, speed, gravity);

            float pitchCmd;
            if (pitchOwned)
            {
                _pitchMode = Mode.Off;
                _pitchRef = pitchDeg;
                pitchCmd = _rate[Pitch];
            }
            else if (pitchActive)
            {
                _pitchMode = Mode.Manual;
                _pitchRef = pitchDeg;
                pitchCmd = PilotPitch(_input[Pitch], pitchComp, speed, gravity, pitchDeg, bankDeg);
            }
            else
            {
                if (_pitchMode == Mode.Manual || _pitchMode == Mode.Off)
                {
                    _pitchMode = Mode.Capture;
                    _pitchCapture = 0f;
                }

                pitchCmd = PitchHold(pitchDeg, phi, pitchComp, dt);
            }

            bool limited = false;
            if (!pitchOwned)
            {
                float protectedCmd = Envelope.Limit(pitchCmd, aoa, speed, gravity, pitchDeg, bankDeg, _loadLimited, _maxLoad, _minLoad);
                if (Mathf.Abs(protectedCmd - pitchCmd) > 1e-4f)
                {
                    limited = true;
                    if (_pitchMode == Mode.Hold)
                    {
                        // Do not wind up against the envelope.
                        _pitchRef = pitchDeg;
                    }
                }

                pitchCmd = protectedCmd;
            }

            float rollCmd;
            if (rollOwned)
            {
                _bankMode = Mode.Off;
                _bankRef = bankDeg;
                rollCmd = _rate[Roll];
            }
            else if (rollActive)
            {
                _bankMode = Mode.Manual;
                _bankRef = bankDeg;
                rollCmd = _input[Roll] * RollRate * profile;
            }
            else
            {
                if (_bankMode == Mode.Manual || _bankMode == Mode.Off)
                {
                    _bankMode = Mode.Capture;
                    _bankCapture = 0f;
                }

                rollCmd = BankHold(pitchDeg, bankDeg, theta, turnRate, dt);
            }

            bool bankHeld = !rollOwned && !rollActive && _bankMode == Mode.Hold;
            float crossfeed = 0f;
            if (airborne && !rollOwned)
            {
                LearnDihedral(beta, bankHeld, dt);
                crossfeed = _dihedral * beta;
            }

            float coordRate = airborne ? YawControl.CoordinatedRate(vessel, _rate[Roll], speed) : 0f;
            float slipError = airborne ? beta : 0f;

            if (pitchOwned)
            {
                _pitchLoop.Hold();
            }
            else
            {
                ctrl.pitch = _pitchLoop.Step(pitchCmd, _rate[Pitch], qRatio, 0f, pitchActive, dt);
            }

            if (rollOwned)
            {
                _rollLoop.Hold();
            }
            else
            {
                ctrl.roll = _rollLoop.Step(rollCmd, _rate[Roll], qRatio, crossfeed, rollActive || pedalActive, dt);
            }

            ctrl.yaw = _yaw.Step(_input[Yaw], coordRate, _rate[Yaw], slipError, qRatio, pedalActive, dt);

            if (_recorder.Tick(dt))
            {
                const float deg = Mathf.Rad2Deg;
                _recorder.Record(
                    vessel.GetIasKt(), qRatio, aoa, gee, pitchDeg, bankDeg, beta * deg, pitchDot, bankDot,
                    pitchIn, _input[Pitch], (float)_pitchMode, _pitchRef, pitchCmd * deg, _rate[Pitch] * deg,
                    _pitchLoop.Output, _pitchLoop.Integral, _pitchLoop.Gain, _pitchLoop.Effectiveness,
                    rollIn, _input[Roll], (float)_bankMode, _bankRef, rollCmd * deg, _rate[Roll] * deg,
                    _rollLoop.Output, _rollLoop.Integral, _rollLoop.Gain, _rollLoop.Effectiveness, crossfeed, _dihedral,
                    yawIn, _input[Yaw], coordRate * deg, _rate[Yaw] * deg,
                    _yaw.Output, _yaw.Gain, headingDeg, limited ? 1f : 0f);
            }
        }

        // Overrides the rudder the lateral modes write after Apply.
        public void ReassertYaw(FlightCtrlState ctrl)
        {
            if (_ready)
            {
                ctrl.yaw = _yaw.Output;
            }
        }

        private void Engage(Vessel vessel, FlightCtrlState ctrl, Vector3 body)
        {
            bool sameVessel = vessel.id == _vesselId;
            _vesselId = vessel.id;
            _ready = true;
            _pitchMode = Mode.Capture;
            _bankMode = Mode.Capture;
            _pitchCapture = 0f;
            _bankCapture = 0f;
            _pitchRef = vessel.GetPitch();
            _bankRef = vessel.GetBank();
            _lastPitch = _pitchRef;
            _lastBank = _bankRef;

            if (!sameVessel)
            {
                _dihedral = DihedralPrior;
            }

            for (int i = 0; i < 3; i++)
            {
                _input[i] = 0f;
            }

            _rate[Pitch] = body.x;
            _rate[Roll] = body.y;
            _rate[Yaw] = body.z;
            _pitchLoop.Reset(ctrl.pitch);
            _rollLoop.Reset(ctrl.roll);
            _yaw.Reset(ctrl.yaw);
            _rollTrim = _rollLoop.Integral;
            _recorder.Start(vessel);
        }

        private void Disarm()
        {
            if (!_ready)
            {
                return;
            }

            _ready = false;
            _recorder.Stop();
        }

        private float PitchHold(float pitchDeg, float phi, float pitchComp, float dt)
        {
            if (_pitchMode == Mode.Capture)
            {
                _pitchRef = pitchDeg;
                _pitchCapture += dt;
                if (Mathf.Abs(_rate[Pitch] - pitchComp) < CaptureRate || _pitchCapture > CaptureTimeout)
                {
                    _pitchMode = Mode.Hold;
                }

                return pitchComp;
            }

            // Elevator no longer moves the pitch angle near the vertical or at steep bank.
            float cosPhi = Mathf.Cos(phi);
            if (Mathf.Abs(pitchDeg) > MaxHoldPitch || Mathf.Abs(cosPhi) < 0.5f)
            {
                _pitchRef = pitchDeg;
                return pitchComp;
            }

            float error = Mathf.DeltaAngle(pitchDeg, _pitchRef) * Mathf.Deg2Rad;
            float thetaDot = Mathf.Clamp(PitchHoldGain * error, -PitchHoldRate, PitchHoldRate);
            return (thetaDot * cosPhi) + pitchComp;
        }

        // Neutral stick keeps the turn-compensated rate; full stick asks for the load limit, or the rate cap without one.
        private float PilotPitch(float input, float comp, float speed, float gravity, float pitchDeg, float bankDeg)
        {
            if (input >= 0f)
            {
                float up = _loadLimited ? Envelope.MaxPitchRate(speed, gravity, pitchDeg, bankDeg, _maxLoad) : Envelope.RateCap;
                return Mathf.Lerp(comp, up, input);
            }

            float down = _loadLimited ? Envelope.MinPitchRate(speed, gravity, pitchDeg, bankDeg, _minLoad) : -Envelope.RateCap;
            return Mathf.Lerp(comp, down, -input);
        }

        private float BankHold(float pitchDeg, float bankDeg, float theta, float turnRate, float dt)
        {
            if (_bankMode == Mode.Capture)
            {
                _bankRef = bankDeg;
                _bankCapture += dt;
                if (Mathf.Abs(_rate[Roll]) < CaptureRate * 2f || _bankCapture > CaptureTimeout)
                {
                    _bankMode = Mode.Hold;
                    if (Mathf.Abs(bankDeg) < WingsLevel)
                    {
                        _bankRef = 0f;
                    }
                }

                return 0f;
            }

            if (Mathf.Abs(pitchDeg) > MaxHoldPitch || Mathf.Abs(_bankRef) > MaxHoldBank)
            {
                _bankRef = bankDeg;
                return 0f;
            }

            // p = phiDot - psiDot sin(theta)
            float error = Mathf.DeltaAngle(bankDeg, _bankRef) * Mathf.Deg2Rad;
            float phiDot = Mathf.Clamp(BankHoldGain * error, -BankHoldRate, BankHoldRate);
            return phiDot - (turnRate * Mathf.Sin(theta));
        }

        // Moves the steady roll integral offset from trim into the sideslip crossfeed.
        private void LearnDihedral(float beta, bool bankHeld, float dt)
        {
            if (!bankHeld || Mathf.Abs(_rate[Roll]) > DihedralSteadyRate)
            {
                return;
            }

            float slip = Mathf.Abs(beta);
            if (slip < DihedralMinSlip * 0.3f)
            {
                _rollTrim = Filter.Blend(_rollTrim, _rollLoop.Integral, dt, DihedralTrimTau);
                return;
            }

            if (slip < DihedralMinSlip)
            {
                return;
            }

            float observed = _dihedral + ((_rollLoop.Integral - _rollTrim) / beta);
            _dihedral = Mathf.Clamp(Filter.Blend(_dihedral, observed, dt, DihedralLearnTau), DihedralMin, DihedralMax);
        }

        private void ShapeInput(int axis, float target, float dt)
        {
            float current = _input[axis];
            bool reversing = current * target < 0f;
            float goal = reversing ? 0f : target;
            bool opening = !reversing && Mathf.Abs(target) > Mathf.Abs(current);
            float rate = (opening ? 1f : CloseRatio) / _smoothing;
            float ease = Mathf.Abs(goal - current) * (1f - Mathf.Exp(-dt / (EaseRatio * _smoothing)));
            float step = Mathf.Min(rate * dt, Mathf.Max(ease, 0.002f));
            _input[axis] = Mathf.MoveTowards(current, goal, step);
        }

        private static float PrecisionScale()
        {
            FlightInputHandler input = FlightInputHandler.fetch;
            if (input != null && input.precisionMode)
            {
                return Precision;
            }

            return 1f;
        }
    }
}
