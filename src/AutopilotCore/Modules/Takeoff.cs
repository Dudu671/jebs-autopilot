using System.Collections.Generic;
using UnityEngine;

namespace AutopilotCore
{
    // Takes off along the line the vessel stands on: a KSC runway it is lined up with, or else its
    // heading from where it stands. Full power on the centerline, rotation at VR to an attitude short of
    // a tail strike, climb on the extended centerline at V2 with the gear up, and at 4000 ft hands over:
    // Heading Hold on the heading flown, Altitude Hold climbing to 7000 ft at 2000 fpm, Speed Hold at V2 + 50 kt.
    // The VR field starts with the speed from the lift model and follows it until edited.
    public sealed class Takeoff : IFlightProcedure
    {
        private enum Phase
        {
            Roll,
            Rotate,
            Climb,
            Reject,
        }

        // Lined up on a KSC runway: this close to its centerline and course.
        private const float LineUpCrossTrack = 40f;
        private const float LineUpHeading = 30f;

        private const float SpoolRate = 0.5f;
        private const float RotateRate = 3f;
        private const float MaxRotatePitch = 12f;
        private const float TailStrikeMargin = 2f;

        // VR keeps a margin over the stall and over the liftoff speed at the rotation attitude;
        // V2 over the stall and over VR.
        private const float VrStallFactor = 1.1f;
        private const float VrLiftoffFactor = 1.05f;
        private const float V2StallFactor = 1.2f;
        private const float V2VrFactor = 1.1f;

        // The rotation attitude is held until the tail is clear, then speed is flown on pitch.
        private const float TailClearHeight = 35f * Units.MetersPerFoot;
        private const float GearUpHeight = 50f * Units.MetersPerFoot;
        private const float GearUpVs = 1f;

        // Altitudes (ft) are above sea level.
        private const float HandoverAltitudeFt = 4000f;
        private const float CruiseAltitudeFt = 7000f;
        private const float CruiseVsFpm = 2000f;
        private const float CruiseMarginKt = 50f;

        // Speed on pitch: climb rate (m/s) per kt above the target, its integral per kt*s, and the limits.
        private const float SpeedToVs = 0.15f;
        private const float SpeedToVsIntegral = 0.03f;
        private const float MinClimbVs = 1f;
        private const float MaxClimbVs = 15f;
        private const float ThrustModeMarginKt = 5f;
        private const float MaxClimbPitch = 20f;
        private const float MinPitch = -5f;

        private const float ClimbBank = 15f;
        private const float RotateBank = 5f;
        private const float MinGuidanceSpeed = 10f;

        // On a KSC runway the takeoff is checked at the last point it could still stop: braking at this
        // deceleration with this margin to spare. If VR would come too late it is rejected there.
        private const float BrakeDecel = 2f;
        private const float StopMargin = 150f;
        private const float DecisionWindow = 100f;
        private const float RotateTime = 3f;
        private const float AccelTau = 1f;
        private const float StoppedSpeed = 0.5f;

        private const float EstimateInterval = 0.5f;

        private readonly LiftModel _lift = new LiftModel();
        private readonly GroundGeometry _ground = new GroundGeometry();
        private readonly RunwaySteering _steering = new RunwaySteering();
        private readonly VerticalSpeedControl _vertical = new VerticalSpeedControl("Takeoff");
        private readonly BankController _bank = new BankController();
        private readonly YawControl _yaw = new YawControl("Takeoff yaw");
        private readonly ThrottleControl _throttle = new ThrottleControl();
        private readonly FloatField _vrField = new FloatField(0f);
        private readonly FlightRecorder _recorder = new FlightRecorder(
            "takeoff.csv",
            "t,phase,along_m,xtrack_m,hdg,desired_trk,height_ft,ias,vr,v2,ias_target,pitch,pitch_cmd,rotate_pitch,vs_fpm,vs_cmd_fpm," +
            "throttle,thrust_limited,bank,rudder,steer,stall_kt,liftoff_kt,projected_m,lift_check",
            true);

        private HeadingHold _headingHold;
        private AltitudeHold _altitudeHold;
        private SpeedHold _speedHold;

        private bool _vrEdited;
        private bool _active;
        private Phase _phase;
        private string _status = "";

        private float _nextEstimate;
        private float _rotatePitch = MaxRotatePitch;
        private float _stallKt = float.NaN;
        private float _liftoffKt = float.NaN;
        private float _calculatedVrKt = float.NaN;
        private float _vr;
        private float _v2;

        private string _runwayName = "";
        private double _originLat;
        private double _originLon;
        private float _course;
        private float _runwayLength;
        private double _elevation;

        private float _along;
        private float _cross;
        private float _height;
        private float _throttleOut;
        private float _groundPitch;
        private float _pitchTarget;
        private float _climbVs;
        private float _vsCmd;
        private float _speedTarget;
        private float _desiredTrack;
        private float _accel;
        private float _lastGroundSpeed;
        private float _projected = float.NaN;
        private bool _committed;
        private bool _gearUp;
        private bool _brakes;
        private bool _thrustLimited;

        public string Name
        {
            get { return "Takeoff"; }
        }

        public bool Enabled { get; set; }

        public string Summary
        {
            get
            {
                switch (_phase)
                {
                    case Phase.Rotate: return "TO ROTATE";
                    case Phase.Climb: return "TO CLIMB " + _speedTarget.ToString("F0") + " kt";
                    case Phase.Reject: return "TO REJECT";
                    default: return "TO VR " + _vr.ToString("F0") + " kt";
                }
            }
        }

        public string LateralLabel
        {
            get { return _phase == Phase.Climb ? "TRK" : "RWY"; }
        }

        public string LateralValue
        {
            get { return _phase != Phase.Climb && _runwayName.Length > 0 ? _runwayName : Format.Heading(_course); }
        }

        public string VerticalLabel
        {
            get
            {
                switch (_phase)
                {
                    case Phase.Rotate: return "ROT";
                    case Phase.Climb: return "CLB";
                    case Phase.Reject: return "RTO";
                    default: return "TO";
                }
            }
        }

        public float SpeedTargetKt
        {
            get { return _phase == Phase.Climb ? _speedTarget : _phase == Phase.Reject ? 0f : _vr; }
        }

        public bool OwnsRudder
        {
            get { return Enabled && _active && _phase != Phase.Climb; }
        }

        public bool TryGetDesiredTrack(out float track)
        {
            track = _desiredTrack;
            return _active;
        }

        // The modes that take over once the climb is established.
        public void Bind(HeadingHold heading, AltitudeHold altitude, SpeedHold speed)
        {
            _headingHold = heading;
            _altitudeHold = altitude;
            _speedHold = speed;
        }

        private float Vr
        {
            get { return _vrField.Value; }
        }

        private float V2
        {
            get
            {
                float vr = Vr;
                return float.IsNaN(_stallKt) ? V2VrFactor * vr : Mathf.Max(V2StallFactor * _stallKt, V2VrFactor * vr);
            }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (vessel == null || vessel.mainBody == null)
            {
                return;
            }

            // The speeds are shown before engaging, and follow the vessel until it rotates.
            if (!_active || _phase == Phase.Roll)
            {
                Estimate(vessel);
            }

            if (!Enabled)
            {
                if (_active)
                {
                    Disengage(vessel, ctrl);
                }

                return;
            }

            if (!_active && !Engage(vessel))
            {
                Enabled = false;
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            Locate(vessel);
            UpdatePhase(vessel, dt);
            switch (_phase)
            {
                case Phase.Roll:
                    Roll(vessel, ctrl, dt);
                    break;
                case Phase.Rotate:
                    Rotate(vessel, ctrl, dt);
                    break;
                case Phase.Climb:
                    Climb(vessel, ctrl, dt);
                    break;
                case Phase.Reject:
                    Reject(vessel, ctrl, dt);
                    break;
            }

            if (_active && _recorder.Tick(dt))
            {
                const float ft = Units.FeetPerMeter;
                float pitchCmd = _phase == Phase.Climb ? _vertical.PitchCommand : _pitchTarget;
                _recorder.Record((float)_phase, _along, _cross, vessel.GetHeading(), _desiredTrack, _height * ft, vessel.GetAeroIasKt(), _vr, _v2, SpeedTargetKt,
                    vessel.GetPitch(), pitchCmd, _rotatePitch, (float)vessel.verticalSpeed * Units.FpmPerMps, _vsCmd * Units.FpmPerMps,
                    ctrl.mainThrottle, _thrustLimited ? 1f : 0f, vessel.GetBank(), ctrl.yaw, ctrl.wheelSteer, _stallKt, _liftoffKt, _projected, _lift.Check);
            }

            if (_phase == Phase.Climb && _gearUp && vessel.GetAltitudeAslFt() >= HandoverAltitudeFt)
            {
                HandOver(vessel);
            }
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            if (_vrField.Draw("VR (kt)"))
            {
                _vrEdited = true;
            }

            GUILayout.Label(CaptionText(), GuiStyles.Caption);

            if (!_active)
            {
                if (_status.Length > 0)
                {
                    GUILayout.Label(_status);
                }

                return;
            }

            string runway = float.IsInfinity(_runwayLength) ? "" : "   RWY left " + (_runwayLength - _along).ToString("F0") + " m";
            GUILayout.Label(_phase + "   HGT " + (_height * Units.FeetPerMeter).ToString("F0") + " ft   XTK " +
                            _cross.ToString("+0;-0") + " m" + runway);
        }

        private string CaptionText()
        {
            string calculated = float.IsNaN(_calculatedVrKt) ? "unavailable, no lift found on the wings" : _calculatedVrKt.ToString("F0") + " kt";
            return "Calculated VR: " + calculated + "   V2: " + V2.ToString("F0") + " kt";
        }

        private void Estimate(Vessel vessel)
        {
            if (!vessel.LandedOrSplashed || Time.fixedTime < _nextEstimate)
            {
                return;
            }

            _nextEstimate = Time.fixedTime + EstimateInterval;
            _ground.Update(vessel);
            _rotatePitch = Mathf.Min(MaxRotatePitch, _ground.TailStrikePitch - TailStrikeMargin);
            _lift.Update(vessel, _rotatePitch);
            if (!_lift.Valid)
            {
                _stallKt = float.NaN;
                _liftoffKt = float.NaN;
                _calculatedVrKt = float.NaN;
                return;
            }

            _stallKt = Ias(vessel, _lift.StallTas);
            _liftoffKt = Ias(vessel, _lift.LiftoffTas);
            _calculatedVrKt = Mathf.Max(VrStallFactor * _stallKt, VrLiftoffFactor * _liftoffKt);
            float rounded = Mathf.Round(_calculatedVrKt);
            if (!_vrEdited && _vrField.Value != rounded)
            {
                _vrField.Value = rounded;
            }
        }

        private static float Ias(Vessel vessel, float tas)
        {
            return (float)VesselExtensions.CalibratedAirspeed(vessel, tas) * Units.KnotsPerMps;
        }

        private bool Engage(Vessel vessel)
        {
            if (!vessel.LandedOrSplashed)
            {
                _status = "Takeoff starts on the ground.";
                return false;
            }

            if (!EnginesRunning(vessel))
            {
                _status = "No engine running. Stage the engines first.";
                return false;
            }

            _nextEstimate = 0f;
            Estimate(vessel);
            if (float.IsNaN(Vr) || Vr <= 0f)
            {
                _status = "No VR: set it by hand.";
                return false;
            }

            LineUp(vessel);
            _active = true;
            _phase = Phase.Roll;
            _status = "";
            _vr = Vr;
            _v2 = V2;
            _throttleOut = vessel.ctrlState.mainThrottle;
            _groundPitch = vessel.GetPitch();
            _pitchTarget = _groundPitch;
            _accel = 0f;
            _lastGroundSpeed = (float)vessel.horizontalSrfSpeed;
            _projected = float.NaN;
            _committed = false;
            _gearUp = false;
            _brakes = false;
            _vertical.Reset();
            _bank.Reset();
            _yaw.Release();
            _steering.Reset();
            _throttle.Reset();
            _thrustLimited = false;
            vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            _recorder.Start(vessel);
            return true;
        }

        // Stopped on the ground the throttle goes to idle, so a cancelled takeoff does not keep accelerating.
        private void Disengage(Vessel vessel, FlightCtrlState ctrl)
        {
            if (vessel.LandedOrSplashed)
            {
                ThrottleControl.Apply(vessel, ctrl, 0f);
            }

            _active = false;
            _vertical.Reset();
            _bank.Reset();
            _yaw.Release();
            _recorder.Stop();
        }

        private void HandOver(Vessel vessel)
        {
            float heading = Mathf.Round(vessel.GetHeading());
            float speed = Mathf.Round(_v2 + CruiseMarginKt);
            _status = "Handed over at " + vessel.GetAltitudeAslFt().ToString("F0") + " ft: HDG " + Format.Heading(heading) + ", ALT " +
                      CruiseAltitudeFt.ToString("F0") + " ft at " + CruiseVsFpm.ToString("F0") + " fpm, SPD " + speed.ToString("F0") + " kt.";
            Enabled = false;
            _active = false;
            _vertical.Reset();
            _bank.Reset();
            _yaw.Release();
            _recorder.Stop();

            if (_headingHold != null)
            {
                _headingHold.TargetHeading = heading;
                _headingHold.Enabled = true;
            }

            if (_altitudeHold != null)
            {
                _altitudeHold.TargetFt = CruiseAltitudeFt;
                _altitudeHold.VsLimitFpm = CruiseVsFpm;
                _altitudeHold.Enabled = true;
            }

            if (_speedHold != null)
            {
                _speedHold.TargetKt = speed;
                _speedHold.Enabled = true;
            }
        }

        private static bool EnginesRunning(Vessel vessel)
        {
            List<ModuleEngines> engines = vessel.FindPartModulesImplementing<ModuleEngines>();
            for (int i = 0; i < engines.Count; i++)
            {
                if (engines[i].EngineIgnited && !engines[i].flameout)
                {
                    return true;
                }
            }

            return false;
        }

        // The KSC runway the vessel is lined up with, or else its heading from where it stands.
        private void LineUp(Vessel vessel)
        {
            double radius = vessel.mainBody.Radius;
            float heading = vessel.GetHeading();
            _runwayName = "";
            _originLat = vessel.latitude;
            _originLon = vessel.longitude;
            _course = heading;
            _runwayLength = float.PositiveInfinity;
            if (vessel.mainBody.isHomeWorld)
            {
                for (int i = 0; i < Runway.Ksc.Length; i++)
                {
                    Runway runway = Runway.Ksc[i];
                    Vector2 offset = NavMath.LocalOffset(radius, runway.ThresholdLat, runway.ThresholdLon, vessel.latitude, vessel.longitude);
                    Vector2 along = Along(runway.Course);
                    float distance = Vector2.Dot(offset, along);
                    float cross = Vector2.Dot(offset, Right(along));
                    float length = (float)runway.Length(radius);
                    if (Mathf.Abs(cross) < LineUpCrossTrack && distance > -LineUpCrossTrack && distance < length &&
                        Mathf.Abs(Mathf.DeltaAngle(heading, runway.Course)) < LineUpHeading)
                    {
                        _runwayName = runway.Name;
                        _originLat = runway.ThresholdLat;
                        _originLon = runway.ThresholdLon;
                        _course = runway.Course;
                        _runwayLength = length;
                        break;
                    }
                }
            }

            _ground.Update(vessel);
            _elevation = _ground.LowestAltitude;
            _desiredTrack = _course;
        }

        // Along: meters past the origin. Cross: meters right of the centerline. Height: lowest point above the runway.
        private void Locate(Vessel vessel)
        {
            Vector2 offset = NavMath.LocalOffset(vessel.mainBody.Radius, _originLat, _originLon, vessel.latitude, vessel.longitude);
            Vector2 along = Along(_course);
            _along = Vector2.Dot(offset, along);
            _cross = Vector2.Dot(offset, Right(along));
            _ground.Update(vessel);
            _height = (float)(_ground.LowestAltitude - _elevation);
        }

        private static Vector2 Along(float course)
        {
            float rad = course * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        private static Vector2 Right(Vector2 along)
        {
            return new Vector2(along.y, -along.x);
        }

        private void UpdatePhase(Vessel vessel, float dt)
        {
            switch (_phase)
            {
                case Phase.Roll:
                    _vr = Vr;
                    _v2 = V2;
                    if (vessel.GetAeroIasKt() >= _vr)
                    {
                        _phase = Phase.Rotate;
                        _pitchTarget = vessel.GetPitch();
                    }
                    else if (RunwayTooShort(vessel, dt))
                    {
                        _phase = Phase.Reject;
                        _status = "Rejected: VR would come " + (_projected - _runwayLength).ToString("F0") + " m past the runway end.";
                    }

                    break;

                case Phase.Rotate:
                    if (!vessel.LandedOrSplashed && _height > TailClearHeight)
                    {
                        _phase = Phase.Climb;
                        _vsCmd = (float)vessel.verticalSpeed;
                        _climbVs = Mathf.Clamp(_vsCmd, MinClimbVs, MaxClimbVs);
                        _vertical.Reset();
                    }

                    break;

                case Phase.Climb:
                    if (!_gearUp && _height > GearUpHeight && vessel.verticalSpeed > GearUpVs)
                    {
                        vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false);
                        _gearUp = true;
                    }

                    break;
            }
        }

        // At the last point it could still stop, rejects if VR, reached at the current acceleration, comes too late.
        private bool RunwayTooShort(Vessel vessel, float dt)
        {
            float groundSpeed = (float)vessel.horizontalSrfSpeed;
            _accel = Filter.Blend(_accel, (groundSpeed - _lastGroundSpeed) / dt, dt, AccelTau);
            _lastGroundSpeed = groundSpeed;
            if (_committed || float.IsInfinity(_runwayLength))
            {
                return false;
            }

            float vr = _vr / Units.KnotsPerMps;
            float toVr = _accel > 0.05f ? Mathf.Max(0f, (vr * vr) - (groundSpeed * groundSpeed)) / (2f * _accel) : float.PositiveInfinity;
            _projected = _along + toVr + (RotateTime * vr);
            float stop = (groundSpeed * groundSpeed / (2f * BrakeDecel)) + StopMargin;
            if (_runwayLength - _along - stop > DecisionWindow)
            {
                return false;
            }

            _committed = true;
            return _projected > _runwayLength;
        }

        private void Roll(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            _throttleOut = Mathf.MoveTowards(_throttleOut, 1f, SpoolRate * dt);
            ThrottleControl.Apply(vessel, ctrl, _throttleOut);
            ctrl.roll = 0f;

            // The nose is held on the runway, so it does not lift before VR.
            _pitchTarget = _groundPitch;
            _vertical.Pitch.Step(vessel, ctrl, _groundPitch, dt);
            _desiredTrack = _steering.Steer(vessel, ctrl, _course, _cross, dt);
        }

        private void Rotate(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            _throttleOut = Mathf.MoveTowards(_throttleOut, 1f, SpoolRate * dt);
            ThrottleControl.Apply(vessel, ctrl, _throttleOut);

            _rotatePitch = Mathf.Min(MaxRotatePitch, _ground.TailStrikePitch - TailStrikeMargin);
            _pitchTarget = Mathf.MoveTowards(_pitchTarget, _rotatePitch, RotateRate * dt);
            _vertical.Pitch.Step(vessel, ctrl, _pitchTarget, dt);
            _bank.StepBank(vessel, ctrl, 0f, RotateBank, dt);
            if (vessel.LandedOrSplashed)
            {
                _desiredTrack = _steering.Steer(vessel, ctrl, _course, _cross, dt);
            }
            else
            {
                _desiredTrack = _course;
                ctrl.wheelSteer = 0f;
                ctrl.yaw = _steering.RudderToHeading(vessel, _course, dt);
            }
        }

        private void Climb(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            _speedTarget = _v2;
            float ias = vessel.GetAeroIasKt();
            float excess = ias - _speedTarget;

            // With more thrust than the steepest climb absorbs, the throttle holds the speed at that climb
            // rate, until it is back at full and the speed still falls.
            if (!_thrustLimited && _climbVs >= MaxClimbVs && excess > ThrustModeMarginKt)
            {
                _thrustLimited = true;
                _throttle.Reset();
            }
            else if (_thrustLimited && _throttleOut >= 1f && excess < -ThrustModeMarginKt)
            {
                _thrustLimited = false;
            }

            if (_thrustLimited)
            {
                _throttleOut = _throttle.Step(ias, _speedTarget, _throttleOut, dt);
                _vsCmd = MaxClimbVs;
            }
            else
            {
                // Faster than the target, climb steeper; the integral carries the climb rate the thrust allows.
                _throttleOut = Mathf.MoveTowards(_throttleOut, 1f, SpoolRate * dt);
                _climbVs = Mathf.Clamp(_climbVs + (SpeedToVsIntegral * excess * dt), MinClimbVs, MaxClimbVs);
                _vsCmd = Mathf.Clamp(_climbVs + (SpeedToVs * excess), MinClimbVs, MaxClimbVs);
            }

            ThrottleControl.Apply(vessel, ctrl, _throttleOut);
            _vertical.Step(vessel, ctrl, _vsCmd, MinPitch, MaxClimbPitch, dt);

            float groundSpeed = (float)vessel.horizontalSrfSpeed;
            float track = groundSpeed > MinGuidanceSpeed ? vessel.GetTrack() : vessel.GetHeading();
            float airSpeed = Mathf.Max(groundSpeed, (float)vessel.srfSpeed);
            float l1;
            _desiredTrack = L1Guidance.DesiredTrack(_course, _cross, groundSpeed, airSpeed, Mathf.Max(vessel.GetGravity(), 0.1f), ClimbBank, out l1);
            _bank.Step(vessel, ctrl, track, _desiredTrack, ClimbBank, dt);
            _yaw.Coordinate(vessel, ctrl, dt);
        }

        private void Reject(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            _throttleOut = 0f;
            ThrottleControl.Apply(vessel, ctrl, _throttleOut);
            ctrl.roll = 0f;
            _vertical.Pitch.Step(vessel, ctrl, _groundPitch, dt);
            _desiredTrack = _steering.Steer(vessel, ctrl, _course, _cross, dt);
            if (!_brakes)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                _brakes = true;
            }

            if (vessel.horizontalSrfSpeed < StoppedSpeed)
            {
                Enabled = false;
            }
        }
    }
}
