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

        private const float PitchRate = 0.6f;
        private const float RollRate = 2.0f;
        private const float Precision = 0.33f;
        private const float Deadband = 0.04f;

        private const float InputOpen = 1.8f;
        private const float InputClose = 4f;
        private const float InputEase = 0.06f;

        // Stick units per second.
        private static readonly float[] Slew = { 2f, 3f, 3f };

        private const float PitchHoldGain = 1.2f;
        private const float BankHoldGain = 1.5f;
        private const float PitchHoldRate = 0.17f;
        private const float BankHoldRate = 0.35f;

        private const float CaptureRate = 0.035f;
        private const float CaptureTimeout = 1.2f;
        private const float WingsLevel = 5f;
        private const float MaxHoldPitch = 70f;
        private const float MaxHoldBank = 75f;

        private const float PedalHandover = 0.15f;

        private const float DihedralPrior = 1f;
        private const float DihedralMin = -2f;
        private const float DihedralMax = 6f;
        private const float DihedralLearnTau = 1.5f;
        private const float DihedralTrimTau = 2f;
        private const float DihedralMinSlip = 0.026f;
        private const float DihedralSteadyRate = 0.05f;

        private const float HeadingSlipGain = 0.5f;
        private const float HeadingSlip = 0.026f;
        private const float HeadingCaptureRate = 0.009f;
        private const float HeadingCaptureTimeout = 3f;

        private const float TurnCompBank = 60f;
        private const float MinCoordSpeed = 20f;

        private const float QReference = 0.5f * 1.223f * 56.6f * 56.6f;
        private const float MinQ = 0.3f;
        private const float MaxQ = 4f;
        private const float IntegralLimit = 0.5f;
        private const float RateTau = 0.03f;

        // Stick per rad/s at QReference; pitch, roll.
        private static readonly float[] Feed = { 0.9f, 0.4f };
        private static readonly float[] Kp = { 1.6f, 0.5f };
        private static readonly float[] Ki = { 1.5f, 1f };

        private const float YawDamp = 2.5f;
        private const float WashoutTau = 2f;
        private const float SlipGain = 3f;
        private const float SlipIntegralGain = 3f;

        private const float MaxAoa = 15f;
        private const float MinAoa = -8f;
        private const float MaxGee = 15f;
        private const float AoaPush = 2f;
        private const float GeePush = 0.04f;

        private const float GuardHalfPeriod = 0.8f;
        private const int GuardFlips = 3;
        private const float GuardCut = 0.8f;
        private const float GuardMin = 0.4f;
        private const float GuardQuiet = 4f;
        private const float GuardRecover = 0.03f;
        private static readonly float[] GuardThreshold = { 0.03f, 0.08f, 0.02f };

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
            "p_in,p_shaped,p_mode,p_ref,p_cmd,p_rate,p_out,p_int,p_gain," +
            "r_in,r_shaped,r_mode,r_ref,r_cmd,r_rate,r_out,r_int,r_gain,r_xfeed,r_dihedral," +
            "y_in,y_shaped,slip_cmd,y_coord,y_rate,y_out,y_int,y_gain," +
            "heading,hdg_ref,hdg_held,limited");

        private readonly float[] _input = new float[3];
        private readonly float[] _rate = new float[3];
        private readonly float[] _cmd = new float[3];
        private readonly float[] _integral = new float[3];
        private readonly float[] _out = new float[3];
        private readonly float[] _gain = new float[3];
        private readonly int[] _guardSign = new int[3];
        private readonly int[] _guardFlips = new int[3];
        private readonly float[] _guardClock = new float[3];
        private readonly float[] _guardQuiet = new float[3];

        private Mode _pitchMode;
        private Mode _bankMode;
        private float _pitchRef;
        private float _bankRef;
        private bool _wingsLevel;
        private float _pitchCapture;
        private float _bankCapture;
        private bool _headingHeld;
        private float _headingRef;
        private float _headingCapture;
        private float _dihedral;
        private float _rollTrim;
        private float _yawWashout;
        private float _lastPitch;
        private float _lastBank;
        private bool _ready;
        private float _lastTick = float.NegativeInfinity;
        private Guid _vesselId;

        public bool Enabled { get; set; }

        public void Apply(Vessel vessel, FlightCtrlState ctrl, bool piloted, bool pitchOwned, bool rollOwned)
        {
            if (!Enabled || vessel == null || vessel.ReferenceTransform == null || vessel.LandedOrSplashed)
            {
                Disarm();
                return;
            }

            if (!ReadBodyRate(vessel, out Vector3 body))
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
            float pitchIn = (ctrl.pitch - ctrl.pitchTrim) * precision;
            float rollIn = (ctrl.roll - ctrl.rollTrim) * precision;
            float yawIn = (ctrl.yaw - ctrl.yawTrim) * precision;
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
            float gravity = (float)FlightGlobals.getGeeForceAtPosition(vessel.CoM).magnitude;
            float aoa = vessel.GetAoA();
            float gee = (float)vessel.geeForce;
            float beta = Sideslip(vessel);
            float qRatio = DynamicRatio(vessel);
            float profile = Mathf.Clamp(Mathf.Sqrt(qRatio), 0.45f, 1.2f);
            ShapeInput(Pitch, pitchIn, dt);
            ShapeInput(Roll, rollIn, dt);
            ShapeInput(Yaw, yawIn, dt);

            // Level turn: psiDot = g tan(phi) / V; holding the nose needs q = psiDot sin(phi) cos(theta).
            float turnRate = 0f;
            float turnPhi = 0f;
            bool airborne = speed > MinCoordSpeed;
            if (airborne && Mathf.Abs(bankDeg) < 90f)
            {
                turnPhi = Mathf.Clamp(bankDeg, -TurnCompBank, TurnCompBank) * Mathf.Deg2Rad;
                turnRate = gravity * Mathf.Tan(turnPhi) / speed;
            }

            float pitchComp = turnRate * Mathf.Sin(turnPhi) * Mathf.Cos(theta);

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
                pitchCmd = (_input[Pitch] * PitchRate * profile) + pitchComp;
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
                float protectedCmd = Protect(pitchCmd, aoa, gee);
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
                    _wingsLevel = false;
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

            float coordRate = 0f;
            float slipCmd = 0f;
            float slipError = 0f;
            bool headingAllowed = airborne && bankHeld && _wingsLevel && !pedalActive && Mathf.Abs(pitchDeg) < MaxHoldPitch;
            if (!headingAllowed)
            {
                ReleaseHeading();
            }

            if (airborne)
            {
                // Zero lateral load: r = g sin(phi) cos(theta) / V, plus p tan(alpha) to roll about the airflow.
                float alpha = Mathf.Clamp(aoa, -20f, 20f) * Mathf.Deg2Rad;
                coordRate = (gravity * Mathf.Sin(phi) * Mathf.Cos(theta) / speed) + (_rate[Roll] * Mathf.Tan(alpha));

                if (headingAllowed)
                {
                    slipCmd = HoldHeading(headingDeg, dt);
                }

                slipError = beta - slipCmd;
            }

            _cmd[Pitch] = pitchCmd;
            _cmd[Roll] = rollCmd;
            _cmd[Yaw] = coordRate;

            StepAxis(Pitch, qRatio, 0f, pitchOwned, pitchActive, dt);
            StepAxis(Roll, qRatio, crossfeed, rollOwned, rollActive || pedalActive, dt);
            StepYaw(qRatio, slipError, pedalActive, dt);

            if (!pitchOwned)
            {
                ctrl.pitch = _out[Pitch];
            }

            if (!rollOwned)
            {
                ctrl.roll = _out[Roll];
            }

            ctrl.yaw = _out[Yaw];

            if (_recorder.Tick(dt))
            {
                const float deg = Mathf.Rad2Deg;
                _recorder.Record(
                    vessel.GetIasKt(), qRatio, aoa, gee, pitchDeg, bankDeg, beta * deg, pitchDot, bankDot,
                    pitchIn, _input[Pitch], (float)_pitchMode, _pitchRef, pitchCmd * deg, _rate[Pitch] * deg,
                    _out[Pitch], _integral[Pitch], _gain[Pitch],
                    rollIn, _input[Roll], (float)_bankMode, _bankRef, rollCmd * deg, _rate[Roll] * deg,
                    _out[Roll], _integral[Roll], _gain[Roll], crossfeed, _dihedral,
                    yawIn, _input[Yaw], slipCmd * deg, coordRate * deg, _rate[Yaw] * deg,
                    _out[Yaw], _integral[Yaw], _gain[Yaw],
                    headingDeg, _headingRef, _headingHeld ? 1f : 0f, limited ? 1f : 0f);
            }
        }

        // Overrides the proportional rudder the lateral modes write after Apply.
        public void ReassertYaw(FlightCtrlState ctrl)
        {
            if (_ready)
            {
                ctrl.yaw = _out[Yaw];
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
            _wingsLevel = false;
            _lastPitch = _pitchRef;
            _lastBank = _bankRef;
            _yawWashout = 0f;
            ReleaseHeading();

            if (!sameVessel)
            {
                _dihedral = DihedralPrior;
            }

            float[] current = { ctrl.pitch, ctrl.roll, ctrl.yaw };
            float[] rate = { body.x, body.y, body.z };
            for (int i = 0; i < 3; i++)
            {
                _input[i] = 0f;
                _rate[i] = rate[i];
                _cmd[i] = 0f;
                // Bumpless engage.
                _integral[i] = Mathf.Clamp(current[i], -IntegralLimit, IntegralLimit);
                _out[i] = current[i];
                _gain[i] = 1f;
                _guardSign[i] = 0;
                _guardFlips[i] = 0;
                _guardClock[i] = 0f;
                _guardQuiet[i] = 0f;
            }

            _rollTrim = _integral[Roll];
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

        private void ReleaseHeading()
        {
            _headingHeld = false;
            _headingCapture = 0f;
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
                        _wingsLevel = true;
                    }
                }

                return 0f;
            }

            if (Mathf.Abs(pitchDeg) > MaxHoldPitch || Mathf.Abs(_bankRef) > MaxHoldBank)
            {
                _bankRef = bankDeg;
                _wingsLevel = false;
                return 0f;
            }

            // p = phiDot - psiDot sin(theta)
            float error = Mathf.DeltaAngle(bankDeg, _bankRef) * Mathf.Deg2Rad;
            float phiDot = Mathf.Clamp(BankHoldGain * error, -BankHoldRate, BankHoldRate);
            return phiDot - (turnRate * Mathf.Sin(theta));
        }

        private float HoldHeading(float headingDeg, float dt)
        {
            if (!_headingHeld)
            {
                _headingRef = headingDeg;
                _headingCapture += dt;
                bool settled = Mathf.Abs(_input[Yaw]) < 0.01f && Mathf.Abs(_rate[Yaw]) < HeadingCaptureRate;
                if (settled || _headingCapture > HeadingCaptureTimeout)
                {
                    _headingHeld = true;
                }

                return 0f;
            }

            // Turning right needs negative sideslip.
            float error = Mathf.DeltaAngle(headingDeg, _headingRef) * Mathf.Deg2Rad;
            return -Mathf.Clamp(HeadingSlipGain * error, -HeadingSlip, HeadingSlip);
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
                _rollTrim = Filter.Blend(_rollTrim, _integral[Roll], dt, DihedralTrimTau);
                return;
            }

            if (slip < DihedralMinSlip)
            {
                return;
            }

            float observed = _dihedral + ((_integral[Roll] - _rollTrim) / beta);
            _dihedral = Mathf.Clamp(Filter.Blend(_dihedral, observed, dt, DihedralLearnTau), DihedralMin, DihedralMax);
        }

        private void StepAxis(int axis, float qRatio, float extraFeed, bool owned, bool pilotActive, float dt)
        {
            if (owned)
            {
                _out[axis] = Mathf.Clamp(_out[axis], -1f, 1f);
                return;
            }

            float q = Mathf.Clamp(qRatio, MinQ, MaxQ);
            float error = _cmd[axis] - _rate[axis];
            float gain = _gain[axis] / q;
            float feed = (Feed[axis] * _cmd[axis] / Mathf.Sqrt(q)) + extraFeed;
            float proportional = Kp[axis] * gain * error;
            float unclamped = feed + proportional + _integral[axis];
            float wanted = Mathf.Clamp(unclamped, -1f, 1f);
            float stick = Mathf.MoveTowards(_out[axis], wanted, Slew[axis] * dt);

            bool atLimit = Mathf.Abs(unclamped) >= 1f && Mathf.Sign(unclamped) == Mathf.Sign(error);
            bool slewing = Mathf.Abs(wanted - stick) > 1e-4f && Mathf.Sign(wanted - stick) == Mathf.Sign(error);
            if (!atLimit && !slewing)
            {
                _integral[axis] = Mathf.Clamp(_integral[axis] + (Ki[axis] * gain * error * dt), -IntegralLimit, IntegralLimit);
            }

            _out[axis] = stick;
            Guard(axis, error, pilotActive, dt);
        }

        // Pedal + washed-out yaw damper + sideslip PI; the slip loop steps aside while the pedal is in.
        private void StepYaw(float qRatio, float slipError, bool pilotActive, float dt)
        {
            float q = Mathf.Clamp(qRatio, MinQ, MaxQ);
            float gain = _gain[Yaw] / q;
            float pedal = _input[Yaw];
            float handover = 1f - Mathf.Clamp01(Mathf.Abs(pedal) / PedalHandover);
            float rateError = _cmd[Yaw] - _rate[Yaw];
            _yawWashout = Filter.Blend(_yawWashout, rateError, dt, WashoutTau);
            float damper = YawDamp * gain * (rateError - _yawWashout);
            float slip = handover * SlipGain * gain * slipError;
            float unclamped = pedal + damper + slip + _integral[Yaw];
            float wanted = Mathf.Clamp(unclamped, -1f, 1f);
            float stick = Mathf.MoveTowards(_out[Yaw], wanted, Slew[Yaw] * dt);

            bool atLimit = Mathf.Abs(unclamped) >= 1f && Mathf.Sign(unclamped) == Mathf.Sign(slipError);
            bool slewing = Mathf.Abs(wanted - stick) > 1e-4f && Mathf.Sign(wanted - stick) == Mathf.Sign(slipError);
            if (!atLimit && !slewing)
            {
                _integral[Yaw] = Mathf.Clamp(_integral[Yaw] + (handover * SlipIntegralGain * gain * slipError * dt), -IntegralLimit, IntegralLimit);
            }

            _out[Yaw] = stick;
            Guard(Yaw, rateError - _yawWashout, pilotActive, dt);
        }

        private void ShapeInput(int axis, float target, float dt)
        {
            float current = _input[axis];
            bool reversing = current * target < 0f;
            float goal = reversing ? 0f : target;
            bool opening = !reversing && Mathf.Abs(target) > Mathf.Abs(current);
            float rate = opening ? InputOpen : InputClose;
            float ease = Mathf.Abs(goal - current) * (1f - Mathf.Exp(-dt / InputEase));
            float step = Mathf.Min(rate * dt, Mathf.Max(ease, 0.002f));
            _input[axis] = Mathf.MoveTowards(current, goal, step);
        }

        // Cuts the axis gain when the error keeps reversing faster than a pilot would.
        private void Guard(int axis, float error, bool pilotActive, float dt)
        {
            _guardClock[axis] += dt;
            if (pilotActive)
            {
                _guardSign[axis] = 0;
                _guardFlips[axis] = 0;
                return;
            }

            int sign = error > GuardThreshold[axis] ? 1 : (error < -GuardThreshold[axis] ? -1 : 0);
            if (sign != 0 && sign != _guardSign[axis])
            {
                if (_guardSign[axis] != 0 && _guardClock[axis] < GuardHalfPeriod)
                {
                    _guardFlips[axis]++;
                    _guardQuiet[axis] = 0f;
                    if (_guardFlips[axis] >= GuardFlips)
                    {
                        _gain[axis] = Mathf.Max(GuardMin, _gain[axis] * GuardCut);
                        _guardFlips[axis] = 0;
                        Debug.Log("[AutopilotCore] FBW axis " + axis + " hunting, gain " + _gain[axis].ToString("F2"));
                    }
                }
                else
                {
                    _guardFlips[axis] = 0;
                }

                _guardSign[axis] = sign;
                _guardClock[axis] = 0f;
            }

            _guardQuiet[axis] += dt;
            if (_guardQuiet[axis] > GuardQuiet)
            {
                _gain[axis] = Mathf.Min(1f, _gain[axis] + (GuardRecover * dt));
            }
        }

        // Past a limit the allowed pitch rate turns negative and pushes the nose back.
        private static float Protect(float cmd, float aoaDeg, float gee)
        {
            float upper = AoaPush * (MaxAoa - aoaDeg) * Mathf.Deg2Rad;
            upper = Mathf.Min(upper, GeePush * (MaxGee - gee));
            float lower = AoaPush * (MinAoa - aoaDeg) * Mathf.Deg2Rad;
            if (lower > upper)
            {
                return 0.5f * (lower + upper);
            }

            return Mathf.Clamp(cmd, lower, upper);
        }

        // rad/s: x = nose up, y = right wing down, z = nose right.
        // Uses the world-space rigidbody rate; vessel.angularVelocity is already vessel-local.
        // Unity is left-handed, hence the negations.
        private static bool ReadBodyRate(Vessel vessel, out Vector3 body)
        {
            body = Vector3.zero;
            Rigidbody rb = vessel.rootPart != null ? vessel.rootPart.Rigidbody : null;
            if (rb == null)
            {
                return false;
            }

            Vector3 local = vessel.ReferenceTransform.InverseTransformDirection(rb.angularVelocity);
            if (float.IsNaN(local.x) || float.IsNaN(local.y) || float.IsNaN(local.z))
            {
                return false;
            }

            body = new Vector3(-local.x, -local.y, -local.z);
            return true;
        }

        // Positive with the air from the right of the nose.
        private static float Sideslip(Vessel vessel)
        {
            Vector3d air = vessel.GetAirVelocity();
            Transform body = vessel.ReferenceTransform;
            double forward = Vector3d.Dot(air, body.up);
            if (air.sqrMagnitude < 1.0 || forward < 0.1)
            {
                return 0f;
            }

            return (float)Math.Atan2(Vector3d.Dot(air, body.right), forward);
        }

        private static float DynamicRatio(Vessel vessel)
        {
            float speed = Mathf.Max((float)vessel.GetAirVelocity().magnitude, 1f);
            float density = Mathf.Max((float)vessel.atmDensity, 0f);
            float pressure = 0.5f * density * speed * speed;
            return Mathf.Clamp(pressure / QReference, 0.08f, 5f);
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
