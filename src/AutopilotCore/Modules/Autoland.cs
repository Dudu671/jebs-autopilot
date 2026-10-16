using UnityEngine;

namespace AutopilotCore
{
    // Lands on a KSC runway from anywhere. Without room for a straight-in, it flies a downwind leg at
    // pattern height two turn radii beside the centerline and turns final abeam a point short of the
    // FAF, descending to 1000 ft at the FAF. Then it follows the 3 degree glide path, flares within the
    // tail clearance, and keeps the centerline braking to a stop, with reverse thrust when the engines have
    // it. Heights are of the vessel's lowest point above the runway. The Vref field starts with the speed
    // from the lift model and follows it until edited.
    public sealed class Autoland : IFlightProcedure
    {
        private enum Phase
        {
            Position,
            Intercept,
            Glideslope,
            Flare,
            Rollout,
        }

        private const float GlideSlopeDeg = 3f;
        private const float FafHeight = 1000f * Units.MetersPerFoot;

        // Touchdown aim point past the threshold; with the 3 degree path it crosses the threshold at 52 ft.
        private const float AimDistance = 300f;

        // Final is joined this far before the FAF, and the descent from pattern height to the FAF is this steep.
        private const float EstablishMargin = 1500f;
        private const float PatternDescentDeg = 4.5f;

        // The pattern is laid out for this bank, so the turn to final keeps a margin below the approach bank.
        private const float PatternBank = 20f;

        // The turn to final is an arc ending tangent to the centerline; within this angle of the
        // course the localizer takes over, and gives it back only beyond the second angle.
        private const float ArcHandover = 10f;
        private const float ArcResume = 45f;
        private const float MaxArcTurn = 210f;

        private const float ApproachBank = 25f;
        private const float FinalBank = 15f;
        private const float ShortFinalBank = 10f;
        private const float FlareBank = 5f;
        private const float ShortFinalHeight = 60f;
        private const float MinGuidanceSpeed = 10f;

        // Localizer: seconds to close the cross track, the largest track correction it asks for, and the
        // bank (deg) per degree of track error, which turns the track in about 4 s at approach speed.
        private const float LocalizerTau = 20f;
        private const float MaxLocalizerCorrection = 10f;
        private const float TrackToBank = 2f;

        // Track correction (deg) per meter of cross track per second, and its limit.
        private const float CrossTrackIntegral = 0.0008f;
        private const float MaxTrackBias = 5f;

        // The glide path is only joined aligned with the centerline, by the FAF plus a short grace.
        private const float AlignedCrossTrack = 30f;
        private const float AlignedTrackError = 3f;
        private const float AlignGrace = 500f;
        private const float GoAroundCrossTrack = 150f;
        private const float GoAroundTrackError = 15f;
        private const float GoAroundMinHeight = 60f;
        private const float MaxAbovePath = 90f;

        // Terrain: clearance kept over the highest ground under the next minute of flight while
        // positioning, and the share of the glide path height that must stay clear on final.
        private const float TerrainClearance = 500f * Units.MetersPerFoot;
        private const float TerrainLookaheadStep = 10f;
        private const int TerrainLookaheadSteps = 6;
        private const float TerrainCheckInterval = 0.5f;
        private const float FinalTerrainHeight = 120f;
        private const float FinalTerrainShare = 0.5f;

        // Past this far beyond the aim point without touching down, the landing is too long.
        private const float MaxFloat = 600f;

        private const float ApproachMarginKt = 30f;
        private const float FinalMarginKt = 5f;
        private const float VsLimit = 7.5f;
        private const float VsSlew = 0.8f;
        private const float MinPitch = -20f;
        private const float MaxPitch = 25f;

        // Flare: sink rate follows height over the time constant down to the touchdown sink rate.
        private const float FlareTau = 4f;
        private const float MinFlareHeight = 4f;
        private const float MaxFlareHeight = 30f;
        private const float TouchdownSink = 0.3f;
        private const float MaxFlarePitch = 12f;
        private const float TailStrikeMargin = 2f;
        private const float IdleRate = 0.5f;

        private const float NoseLowerRate = 0.4f;

        // Derotation: the attitude target falls at this rate to level, is held, then the elevator is released.
        private const float DerotateRate = 3f;
        private const float NoseDownHold = 1f;
        private const float BrakeDelay = 2f;
        private const float StoppedSpeed = 0.5f;

        // Reverse power once the reversers are out, from a second after touchdown down to this ground speed (m/s).
        private const float ReverseDelay = 1f;
        private const float ReverseCutoff = 20f;
        private const float ReverseThrottle = 1f;

        // Vref keeps a margin over the stall and over the speed that holds the weight at the touchdown attitude.
        private const float VrefStallFactor = 1.3f;
        private const float VrefTouchdownFactor = 1.1f;
        private const float EstimateInterval = 0.5f;

        private static readonly string[] RunwayNames = { Runway.Ksc[0].Name, Runway.Ksc[1].Name };

        private readonly VerticalSpeedControl _vertical = new VerticalSpeedControl("Autoland");
        private readonly BankController _bank = new BankController();
        private readonly YawControl _yaw = new YawControl("Autoland yaw");
        private readonly ThrottleControl _throttle = new ThrottleControl();
        private readonly GroundGeometry _ground = new GroundGeometry();
        private readonly RunwaySteering _steering = new RunwaySteering();
        private readonly LiftModel _lift = new LiftModel();
        private readonly ThrustReversers _reversers = new ThrustReversers();
        private readonly FloatField _vref = new FloatField(0f);
        private readonly FloatField _patternFt = new FloatField(2000f);
        private readonly FlightRecorder _recorder = new FlightRecorder(
            "autoland.csv",
            "t,phase,along_m,xtrack_m,desired_trk,trk_bias,track,hdg,height_ft,path_ft,clearance_ft,floor_ft,vs_fpm,vs_cmd_fpm," +
            "ias,ias_target,bank,beta,pitch,pitch_cmd,tailstrike_pitch,throttle,rudder,steer,brakes,reverser",
            true);

        private int _runwayIndex;
        private Runway _runway;
        private bool _active;
        private Phase _phase;
        private string _status = "";

        private Vector2 _offset;
        private float _along;
        private float _cross;
        private float _height;
        private float _pathHeight;
        private float _desiredTrack;
        private float _vsCmd;
        private float _speedTarget;
        private float _pitchOut;
        private float _throttleOut;
        private float _cruiseHeight;
        private float _descentCap;
        private float _trackBias;
        private float _terrainFloor;
        private float _clearance;
        private float _nextTerrainCheck;
        private string _warning = "";

        private float _side;
        private float _downwindOffset;
        private bool _onFinal;
        private bool _gearDown;
        private bool _brakes;
        private float _groundTime;
        private float _groundPitch;
        private float _derotateTarget;
        private float _derotateHold;
        private bool _noseDown;
        private bool _reverseDone;

        private bool _vrefEdited;
        private float _calculatedVrefKt = float.NaN;
        private float _nextEstimate;

        public string Name
        {
            get { return "Autoland"; }
        }

        public bool Enabled { get; set; }

        public string LateralValue
        {
            get { return Runway.Ksc[_runwayIndex].Name; }
        }

        public float SpeedTargetKt
        {
            get { return _speedTarget; }
        }

        public string LateralLabel
        {
            get
            {
                switch (_phase)
                {
                    case Phase.Position: return "POS";
                    case Phase.Rollout: return "ROLL";
                    default: return "LOC";
                }
            }
        }

        public string VerticalLabel
        {
            get
            {
                switch (_phase)
                {
                    case Phase.Glideslope: return "G/S";
                    case Phase.Flare: return "FLARE";
                    case Phase.Rollout: return "ROLL";
                    default: return "ALT";
                }
            }
        }

        public string Summary
        {
            get { return "LAND " + LateralValue + "  " + LateralLabel + " " + VerticalLabel; }
        }

        // In the flare and on the ground the rudder aligns the nose, so the fly-by-wire yaw damper must not override it.
        public bool OwnsRudder
        {
            get { return Enabled && _active && (_phase == Phase.Flare || _phase == Phase.Rollout); }
        }

        public bool TryGetDesiredTrack(out float track)
        {
            track = _desiredTrack;
            return _active && _phase != Phase.Rollout;
        }

        private static float FafDistance
        {
            get { return FafHeight / Mathf.Tan(GlideSlopeDeg * Mathf.Deg2Rad); }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            // Vref is shown before engaging, and follows fuel burn and gear until the flare.
            if (vessel != null && vessel.mainBody != null && (!_active || _phase < Phase.Flare))
            {
                Estimate(vessel);
            }

            if (!Enabled || vessel == null || vessel.mainBody == null)
            {
                if (_active)
                {
                    Disengage();
                }

                return;
            }

            if (!vessel.mainBody.isHomeWorld)
            {
                _status = "The KSC runway is on Kerbin.";
                Enabled = false;
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            if (!_active && _vref.Value <= 0f)
            {
                _status = "No Vref: set it by hand.";
                Enabled = false;
                return;
            }

            Runway runway = Runway.Ksc[_runwayIndex];
            if (!_active || runway != _runway)
            {
                Engage(vessel, runway);
            }

            Locate(vessel);
            CheckTerrain(vessel);
            UpdatePhase(vessel, ctrl);
            if (_phase == Phase.Rollout)
            {
                Rollout(vessel, ctrl, dt);
            }
            else
            {
                FlyLateral(vessel, ctrl, dt);
                FlyVertical(vessel, ctrl, dt);
                FlySpeed(vessel, ctrl, dt);
            }

            if (_recorder.Tick(dt))
            {
                const float ft = Units.FeetPerMeter;
                _recorder.Record((float)_phase, _along, _cross, _desiredTrack, _trackBias, vessel.GetTrack(), vessel.GetHeading(),
                    _height * ft, _pathHeight * ft, _clearance * ft, _terrainFloor * ft, (float)vessel.verticalSpeed * Units.FpmPerMps,
                    _vsCmd * Units.FpmPerMps, vessel.GetAeroIasKt(), _speedTarget, vessel.GetBank(),
                    vessel.GetSideslip() * Mathf.Rad2Deg, vessel.GetPitch(), _vertical.PitchCommand,
                    Mathf.Min(_ground.TailStrikePitch, 90f), ctrl.mainThrottle,
                    ctrl.yaw, ctrl.wheelSteer, _brakes ? 1f : 0f, _reversers.Progress);
            }
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("KSC runway", GUILayout.Width(110f));
            _runwayIndex = GUILayout.Toolbar(_runwayIndex, RunwayNames, GuiStyles.ToggleButton);
            GUILayout.EndHorizontal();
            if (_vref.Draw("Vref (kt)"))
            {
                _vrefEdited = true;
            }

            string calculated = float.IsNaN(_calculatedVrefKt) ? "unavailable, no lift found on the wings" : _calculatedVrefKt.ToString("F0") + " kt";
            GUILayout.Label("Calculated Vref: " + calculated, GuiStyles.Caption);
            _patternFt.Draw("Pattern (ft)");

            if (!_active)
            {
                if (_status.Length > 0)
                {
                    GUILayout.Label(_status);
                }

                return;
            }

            GUILayout.Label(_phase + "   THR " + ((_along - AimDistance) / 1000f).ToString("F1") + " km   XTK " +
                            _cross.ToString("+0;-0") + " m   AGL " + (_clearance * Units.FeetPerMeter).ToString("F0") + " ft");
            if (_warning.Length > 0)
            {
                GUILayout.Label(_warning);
            }

            if (_phase == Phase.Glideslope || _phase == Phase.Flare)
            {
                GUILayout.Label("G/S " + ((_height - _pathHeight) * Units.FeetPerMeter).ToString("+0;-0") + " ft   HGT " +
                                (_height * Units.FeetPerMeter).ToString("F0") + " ft   SPD " + _speedTarget.ToString("F0") + " kt");
            }
        }

        // Vref from the lift at the stall and at the touchdown attitude, which the tail clearance may hold
        // well below the stall; with the gear up the tail limit is unknown until it comes down.
        private void Estimate(Vessel vessel)
        {
            if (Time.fixedTime < _nextEstimate)
            {
                return;
            }

            _nextEstimate = Time.fixedTime + EstimateInterval;
            _ground.Update(vessel);
            _lift.Update(vessel, Mathf.Min(MaxFlarePitch, _ground.TailStrikePitch - TailStrikeMargin));
            if (!_lift.Valid)
            {
                _calculatedVrefKt = float.NaN;
                return;
            }

            _calculatedVrefKt = Mathf.Max(VrefStallFactor * _lift.StallKt, VrefTouchdownFactor * _lift.AttitudeKt);
            float rounded = Mathf.Round(_calculatedVrefKt);
            if (!_vrefEdited && _vref.Value != rounded)
            {
                _vref.Value = rounded;
            }
        }

        private void Engage(Vessel vessel, Runway runway)
        {
            if (!_active)
            {
                _recorder.Start(vessel);
            }

            _active = true;
            _runway = runway;
            _status = "";
            _gearDown = false;
            _brakes = false;
            _groundTime = 0f;
            _vsCmd = (float)vessel.verticalSpeed;
            _pitchOut = vessel.ctrlState.pitch;
            _throttleOut = vessel.ctrlState.mainThrottle;
            _warning = "";
            _nextTerrainCheck = 0f;
            _vertical.Reset();
            _bank.Reset();
            _yaw.Release();
            _throttle.Reset();
            _steering.Reset();
            Locate(vessel);
            _cruiseHeight = Mathf.Max(_height, FafHeight);
            if (vessel.LandedOrSplashed)
            {
                EnterRollout(vessel);
            }
            else
            {
                EnterPosition(vessel);
            }
        }

        private void Disengage()
        {
            _active = false;
            _vertical.Reset();
            _bank.Reset();
            _yaw.Release();
            _throttle.Reset();
            _recorder.Stop();
        }

        // Along: meters before the aim point, measured along the approach. Cross: meters right of the centerline.
        private void Locate(Vessel vessel)
        {
            double radius = vessel.mainBody.Radius;
            _offset = NavMath.LocalOffset(radius, _runway.ThresholdLat, _runway.ThresholdLon, vessel.latitude, vessel.longitude);
            _along = AimDistance - Vector2.Dot(_offset, Along());
            _cross = Vector2.Dot(_offset, Right());
            _ground.Update(vessel);
            _height = (float)(_ground.LowestAltitude - _runway.Elevation);
            _pathHeight = _along * Mathf.Tan(GlideSlopeDeg * Mathf.Deg2Rad);
        }

        // Clearance over the ground below, and the floor over the highest ground ahead (m above the runway).
        private void CheckTerrain(Vessel vessel)
        {
            CelestialBody body = vessel.mainBody;
            double ground = body.TerrainAltitude(vessel.latitude, vessel.longitude);
            _clearance = (float)(_runway.Elevation + _height - ground);
            if (Time.fixedTime < _nextTerrainCheck)
            {
                return;
            }

            _nextTerrainCheck = Time.fixedTime + TerrainCheckInterval;
            Vector2 velocity = vessel.GetGroundVelocity();
            double highest = ground;
            for (int i = 1; i <= TerrainLookaheadSteps; i++)
            {
                float t = i * TerrainLookaheadStep;
                double lat;
                double lon;
                NavMath.FromLocal(body.Radius, vessel.latitude, vessel.longitude, velocity.x * t, velocity.y * t, out lat, out lon);
                highest = System.Math.Max(highest, body.TerrainAltitude(lat, lon));
            }

            _terrainFloor = (float)(highest - _runway.Elevation) + TerrainClearance;
        }

        private Vector2 Along()
        {
            float course = _runway.Course * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(course), Mathf.Cos(course));
        }

        private Vector2 Right()
        {
            Vector2 along = Along();
            return new Vector2(along.y, -along.x);
        }

        private void UpdatePhase(Vessel vessel, FlightCtrlState ctrl)
        {
            if (vessel.LandedOrSplashed)
            {
                if (_phase != Phase.Rollout)
                {
                    EnterRollout(vessel);
                }

                return;
            }

            if (_phase == Phase.Rollout)
            {
                return;
            }

            float radius = TurnRadius(vessel, PatternBank);
            float trackError = Mathf.Abs(Mathf.DeltaAngle(vessel.GetTrack(), _runway.Course));
            bool aligned = Mathf.Abs(_cross) < AlignedCrossTrack && trackError < AlignedTrackError;
            float abovePath = _height - _pathHeight;
            switch (_phase)
            {
                case Phase.Position:
                    // Straight in when there is room to intercept, otherwise turn final abeam the join point.
                    float join = FafDistance + EstablishMargin;
                    float distance = Mathf.Abs(_cross);
                    bool straightIn = _along >= join + (0.6f * distance) + radius;
                    bool abeam = _along >= join + (0.6f * Mathf.Max(0f, distance - _downwindOffset)) &&
                                 (distance >= radius || trackError < 90f);
                    if (straightIn || abeam)
                    {
                        EnterIntercept();
                    }

                    break;

                case Phase.Intercept:
                    if (aligned && abovePath < MaxAbovePath && (abovePath > -10f || _along <= FafDistance))
                    {
                        _phase = Phase.Glideslope;
                        LowerGear(vessel);
                    }
                    else if (!aligned && (_along < FafDistance - AlignGrace || _along < FafDistance + (0.3f * Mathf.Abs(_cross))))
                    {
                        GoAround(vessel, "not aligned with the centerline at the FAF");
                    }
                    else if (aligned && _along < FafDistance && abovePath >= MaxAbovePath)
                    {
                        GoAround(vessel, "too high at the FAF");
                    }

                    break;

                case Phase.Glideslope:
                    float sink = -(float)vessel.verticalSpeed;
                    float flareHeight = Mathf.Clamp(FlareTau * (sink - TouchdownSink), MinFlareHeight, MaxFlareHeight);
                    if (_along < FafDistance && _height < flareHeight)
                    {
                        // Wind drift is held with bank from here, so the nose can be ruddered onto the centerline.
                        _phase = Phase.Flare;
                        _throttle.Reset();
                        LowerGear(vessel);
                    }
                    else if (_pathHeight > FinalTerrainHeight && _clearance < FinalTerrainShare * _pathHeight)
                    {
                        GoAround(vessel, "terrain below the glide path");
                    }
                    else if (_height > GoAroundMinHeight && (Mathf.Abs(_cross) > GoAroundCrossTrack || trackError > GoAroundTrackError))
                    {
                        GoAround(vessel, "off the centerline");
                    }

                    break;

                case Phase.Flare:
                    if (_along < -MaxFloat)
                    {
                        GoAround(vessel, "no touchdown in the first " + (AimDistance + MaxFloat).ToString("F0") + " m");
                    }

                    break;
            }
        }

        private void GoAround(Vessel vessel, string reason)
        {
            _warning = "Go-around: " + reason + ".";
            EnterPosition(vessel);
        }

        // Downwind on the aircraft's side, two turn radii out, so the 180 degree turn ends on the centerline.
        private void EnterPosition(Vessel vessel)
        {
            _phase = Phase.Position;
            _trackBias = 0f;
            _side = _cross >= 0f ? 1f : -1f;
            _downwindOffset = 2f * TurnRadius(vessel, PatternBank);
        }

        private void EnterIntercept()
        {
            _phase = Phase.Intercept;
            _onFinal = false;
            _trackBias = 0f;
            _descentCap = Mathf.Max(_height, FafHeight);
        }

        // The pitch loop is kept from the flare, so the derotation starts from the elevator already applied.
        private void EnterRollout(Vessel vessel)
        {
            _phase = Phase.Rollout;
            _groundTime = 0f;
            _groundPitch = _pitchOut;
            _derotateTarget = vessel.GetPitch();
            _derotateHold = 0f;
            _noseDown = false;
            _reverseDone = false;
            _reversers.Update(vessel);
            _bank.Reset();
            _yaw.Release();
            _throttle.Reset();
        }

        // At the approach speed, which the aircraft slows to on downwind, not the speed it was engaged at.
        private float TurnRadius(Vessel vessel, float bank)
        {
            float speed = (_vref.Value + ApproachMarginKt) / Units.KnotsPerMps;
            return speed * speed / (Mathf.Max(vessel.GetGravity(), 0.1f) * Mathf.Tan(bank * Mathf.Deg2Rad));
        }

        private float PatternHeight
        {
            get { return Mathf.Max(FafHeight, _patternFt.Value * Units.MetersPerFoot); }
        }

        private void LowerGear(Vessel vessel)
        {
            if (!_gearDown)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, true);
                _gearDown = true;
            }
        }

        private void FlyLateral(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            float groundSpeed = (float)vessel.horizontalSrfSpeed;
            float track = groundSpeed > MinGuidanceSpeed ? vessel.GetTrack() : vessel.GetHeading();
            float airSpeed = Mathf.Max(groundSpeed, (float)vessel.srfSpeed);
            float gravity = Mathf.Max(vessel.GetGravity(), 0.1f);
            float l1;
            if (_phase == Phase.Position)
            {
                // Downwind runs opposite to the landing course, so its right side is the centerline's left.
                float downwindCourse = Mathf.Repeat(_runway.Course + 180f, 360f);
                float downwindCross = (_side * _downwindOffset) - _cross;
                _desiredTrack = L1Guidance.DesiredTrack(downwindCourse, downwindCross, groundSpeed, airSpeed, gravity, ApproachBank, out l1);
                _bank.Step(vessel, ctrl, track, _desiredTrack, ApproachBank, dt);
                _yaw.Coordinate(vessel, ctrl, dt);
                return;
            }

            float turn = ArcTurn(track);
            if (_phase == Phase.Intercept)
            {
                if (turn <= ArcHandover)
                {
                    _onFinal = true;
                }
                else if (turn > ArcResume && turn <= MaxArcTurn)
                {
                    _onFinal = false;
                }

                if (!_onFinal)
                {
                    if (turn <= MaxArcTurn)
                    {
                        _desiredTrack = _runway.Course;
                        _bank.StepBank(vessel, ctrl, ArcBank(turn, groundSpeed, gravity), ApproachBank, dt);
                    }
                    else
                    {
                        // Heading away from the centerline: no single arc reaches it, so turn back toward it first.
                        _desiredTrack = L1Guidance.DesiredTrack(_runway.Course, _cross, groundSpeed, airSpeed, gravity, ApproachBank, out l1);
                        _bank.Step(vessel, ctrl, track, _desiredTrack, ApproachBank, dt);
                    }

                    _yaw.Coordinate(vessel, ctrl, dt);
                    return;
                }
            }

            float maxBank;
            if (_phase == Phase.Intercept)
            {
                maxBank = ApproachBank;
            }
            else if (_phase == Phase.Flare)
            {
                maxBank = FlareBank;
            }
            else
            {
                maxBank = _height > ShortFinalHeight ? FinalBank : ShortFinalBank;
            }

            // A steady side force, such as wind on the drag, otherwise leaves the aircraft beside the centerline.
            // Only once aligned, so the bias does not wind up while the aircraft is still converging.
            float trackError = Mathf.Abs(Mathf.DeltaAngle(track, _runway.Course));
            if (Mathf.Abs(_cross) < AlignedCrossTrack && trackError < AlignedTrackError)
            {
                _trackBias = Mathf.Clamp(_trackBias - (CrossTrackIntegral * _cross * dt), -MaxTrackBias, MaxTrackBias);
            }

            // Localizer: cross track sets a track correction closing it in LocalizerTau, and the track error sets
            // the bank several times faster, so the aircraft settles on the centerline instead of drifting across it.
            float closingSpeed = Mathf.Max(groundSpeed, MinGuidanceSpeed);
            float correction = Mathf.Clamp(-_cross / (closingSpeed * LocalizerTau) * Mathf.Rad2Deg, -MaxLocalizerCorrection, MaxLocalizerCorrection);
            _desiredTrack = Mathf.Repeat(_runway.Course + correction + _trackBias, 360f);
            _bank.StepBank(vessel, ctrl, TrackToBank * Mathf.DeltaAngle(track, _desiredTrack), maxBank, dt);

            // Flare: bank holds the track and the rudder brings the nose onto the centerline, taking out any crab.
            if (_phase == Phase.Flare)
            {
                ctrl.yaw = _steering.RudderToHeading(vessel, _runway.Course, dt);
            }
            else
            {
                _yaw.Coordinate(vessel, ctrl, dt);
            }
        }

        // Degrees still to turn, toward the centerline, to fly along the course: right turns from its
        // right side, left turns from its left.
        private float ArcTurn(float track)
        {
            float offCourse = Mathf.DeltaAngle(_runway.Course, track);
            return Mathf.Repeat(_cross >= 0f ? -offCourse : offCourse, 360f);
        }

        // Bank for the arc through the aircraft, tangent to its track, that ends tangent to the centerline:
        // radius = cross track / (1 - cos turn), recomputed every step so it corrects itself.
        private float ArcBank(float turn, float groundSpeed, float gravity)
        {
            float closing = 1f - Mathf.Cos(turn * Mathf.Deg2Rad);
            if (closing < 1e-4f)
            {
                return 0f;
            }

            float radius = Mathf.Max(Mathf.Abs(_cross) / closing, 1f);
            float bank = Mathf.Atan(groundSpeed * groundSpeed / (gravity * radius)) * Mathf.Rad2Deg;
            return _cross >= 0f ? bank : -bank;
        }

        private void FlyVertical(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            float maxPitch = MaxPitch;
            if (_phase == Phase.Flare)
            {
                _vsCmd = -((_height / FlareTau) + TouchdownSink);
                maxPitch = MaxFlarePitch;
            }
            else
            {
                float wanted;
                if (_phase == Phase.Glideslope)
                {
                    float feedForward = -(float)vessel.horizontalSrfSpeed * Mathf.Tan(GlideSlopeDeg * Mathf.Deg2Rad);
                    wanted = feedForward + VerticalSpeedControl.Capture(_pathHeight - _height, VsLimit);
                }
                else
                {
                    wanted = VerticalSpeedControl.Capture(Mathf.Max(ApproachHeight(), _terrainFloor) - _height, VsLimit);
                }

                _vsCmd = Mathf.MoveTowards(_vsCmd, wanted, VsSlew * dt);
            }

            // Near the runway the nose may not rise past the attitude where the tail touches.
            if (_phase == Phase.Flare || (_phase == Phase.Glideslope && _height < MaxFlareHeight))
            {
                maxPitch = Mathf.Max(MinPitch, Mathf.Min(maxPitch, _ground.TailStrikePitch - TailStrikeMargin));
            }

            _vertical.Step(vessel, ctrl, _vsCmd, MinPitch, maxPitch, dt);
            _pitchOut = ctrl.pitch;
        }

        // Positioning: pattern height, or the engage height brought down along a 3 degree cone toward
        // the FAF, since far from KSC the terrain can be higher than the runway. Turning final: down
        // from there to 1000 ft at the FAF, never climbing.
        private float ApproachHeight()
        {
            if (_phase == Phase.Position)
            {
                float distance = Mathf.Sqrt((_along * _along) + (_cross * _cross));
                float cone = FafHeight + (Mathf.Max(0f, distance - FafDistance) * Mathf.Tan(GlideSlopeDeg * Mathf.Deg2Rad));
                return Mathf.Max(PatternHeight, Mathf.Min(_cruiseHeight, cone));
            }

            float descent = FafHeight + (Mathf.Max(0f, _along - FafDistance) * Mathf.Tan(PatternDescentDeg * Mathf.Deg2Rad));
            return Mathf.Clamp(descent, FafHeight, _descentCap);
        }

        private void FlySpeed(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            bool final = _phase == Phase.Glideslope || _phase == Phase.Flare;
            _speedTarget = _vref.Value + (final ? FinalMarginKt : ApproachMarginKt);
            _throttleOut = _phase == Phase.Flare
                ? Mathf.MoveTowards(_throttleOut, 0f, IdleRate * dt)
                : _throttle.Step(vessel.GetAeroIasKt(), _speedTarget, _throttleOut, dt);
            ThrottleControl.Apply(vessel, ctrl, _throttleOut);
        }

        private void Rollout(Vessel vessel, FlightCtrlState ctrl, float dt)
        {
            _groundTime += dt;
            _speedTarget = 0f;
            float groundSpeed = (float)vessel.horizontalSrfSpeed;
            _throttleOut = ReverseThrust(groundSpeed);
            ThrottleControl.Apply(vessel, ctrl, _throttleOut);
            ctrl.roll = 0f;

            // Main gear ahead of the CoM pitches the nose up on contact, so the attitude is held down actively.
            if (!_noseDown)
            {
                _derotateTarget = Mathf.MoveTowards(_derotateTarget, 0f, DerotateRate * dt);
                float target = Mathf.Min(_derotateTarget, _ground.TailStrikePitch - TailStrikeMargin);
                _vertical.Pitch.Step(vessel, ctrl, target, dt);
                _groundPitch = ctrl.pitch;
                if (_derotateTarget <= 0f)
                {
                    _derotateHold += dt;
                    _noseDown = _derotateHold >= NoseDownHold;
                }
            }
            else
            {
                _groundPitch = Mathf.MoveTowards(_groundPitch, 0f, NoseLowerRate * dt);
                ctrl.pitch = _groundPitch;
            }

            _desiredTrack = _steering.Steer(vessel, ctrl, _runway.Course, _cross, dt);
            if (_groundTime < BrakeDelay)
            {
                return;
            }

            if (!_brakes)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                _brakes = true;
            }

            if (groundSpeed < StoppedSpeed)
            {
                _reversers.Command(false);
                _status = "Stopped on KSC " + _runway.Name + ", brakes set.";
                Enabled = false;
            }
        }

        // The reversers go out on touchdown, and reverse power comes only once they are fully out, so it
        // never pushes forward. Below the cutoff the throttle stays at idle; they are stowed once stopped.
        private float ReverseThrust(float groundSpeed)
        {
            if (!_reversers.Available || _reverseDone)
            {
                return 0f;
            }

            if (groundSpeed < ReverseCutoff)
            {
                _reverseDone = true;
                return 0f;
            }

            _reversers.Command(true);
            return _groundTime >= ReverseDelay && _reversers.Deployed ? ReverseThrottle : 0f;
        }
    }
}
