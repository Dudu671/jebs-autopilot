using System;
using UnityEngine;

namespace AutopilotCore
{
    // Follows the waypoint legs by ground track. A point L1 meters ahead on the leg sets the
    // desired track, and the bank closes on the measured track, so no wind model is needed.
    public sealed class NavHold : ILateralMode
    {
        private const float MinCaptureM = 150f;
        private const float MinGuidanceSpeed = 10f;

        private readonly BankController _bank = new BankController();
        private readonly YawControl _yaw = new YawControl("Nav yaw");
        private readonly FlightRecorder _recorder = new FlightRecorder(
            "nav_hold.csv",
            "t,dist_m,xtrack_m,desired_trk,track,hdg,l1_m,bank,beta,rudder,gs_mps");
        private readonly NavPlan _plan = new NavPlan();
        private int _maxBankIndex = 4;

        private bool _active;
        private int _legIndex = -1;
        private int _legDirectTo;
        private double _legLat;
        private double _legLon;
        private double _fromLat;
        private double _fromLon;
        private float _holdCourse;
        private float _distanceM;
        private float _desiredCourse;
        private float _gravity;

        public string Name
        {
            get { return "Nav"; }
        }

        public bool Enabled { get; set; }

        public NavPlan Plan
        {
            get { return _plan; }
        }

        public string ActiveLabel
        {
            get { return _plan.HasActive ? "WP" + (_plan.ActiveIndex + 1) : "END"; }
        }

        public string Summary
        {
            get { return "NAV " + ActiveLabel; }
        }

        // Ground track the guidance is chasing, available only while engaged on the active waypoint.
        public bool TryGetDesiredTrack(out float track)
        {
            track = _desiredCourse;
            return _active && _plan.HasActive;
        }

        // Start of the leg being tracked, available only while engaged on the active waypoint.
        public bool TryGetLegStart(out double latitude, out double longitude)
        {
            latitude = _fromLat;
            longitude = _fromLon;
            return _active && _plan.HasActive && _legIndex == _plan.ActiveIndex;
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
                _bank.Reset();
                _yaw.Release();
                return;
            }

            bool engaging = !_active;
            _active = true;
            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float maxBank = BankController.MaxBankOptions[_maxBankIndex];
            _gravity = Mathf.Max(vessel.GetGravity(), 0.1f);
            Vector2 groundVel = vessel.GetGroundVelocity();
            float current = groundVel.magnitude > MinGuidanceSpeed ? vessel.GetTrack() : vessel.GetHeading();
            if (engaging)
            {
                _holdCourse = current;
                _recorder.Start(vessel);
            }

            _yaw.Coordinate(vessel, ctrl, dt);
            if (_plan.HasActive && groundVel.magnitude > MinGuidanceSpeed)
            {
                float desiredTrack = GuideToActive(vessel, groundVel, maxBank, engaging, dt);
                _bank.Step(vessel, ctrl, current, desiredTrack, maxBank, dt);
                return;
            }

            _desiredCourse = _plan.HasActive
                ? (float)NavMath.Bearing(vessel.latitude, vessel.longitude, _plan.Active.Latitude, _plan.Active.Longitude)
                : _holdCourse;
            _bank.Step(vessel, ctrl, current, _desiredCourse, maxBank, dt);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            if (_plan.Count == 0)
            {
                GUILayout.Label("No waypoints. Add them on the Navigation Display.");
            }
            else if (!_plan.HasActive)
            {
                GUILayout.Label("Plan complete, holding TRK " + Format.Heading(_holdCourse));
            }
            else if (Enabled)
            {
                GUILayout.Label("To " + NavPlan.NameOf(_plan.ActiveIndex) + "   " + (_distanceM / 1000f).ToString("F1") +
                                " km   TRK " + Format.Heading(_desiredCourse));
            }
            else
            {
                GUILayout.Label("Next: " + NavPlan.NameOf(_plan.ActiveIndex));
            }

            _maxBankIndex = BankController.DrawMaxBankSlider(_maxBankIndex);
        }

        // Ground track to fly so the aircraft converges on the leg.
        private float GuideToActive(Vessel vessel, Vector2 groundVel, float maxBank, bool engaging, float dt)
        {
            double radius = vessel.mainBody.Radius;
            double lat = vessel.latitude;
            double lon = vessel.longitude;
            Waypoint wp = _plan.Active;
            UpdateLegStart(lat, lon, wp, engaging);

            float groundSpeed = groundVel.magnitude;
            float airSpeed = Mathf.Max(HorizontalAirspeed(vessel), groundSpeed);
            _distanceM = (float)NavMath.Distance(radius, lat, lon, wp.Latitude, wp.Longitude);
            float crossTrack = (float)NavMath.CrossTrack(radius, _fromLat, _fromLon, wp.Latitude, wp.Longitude, lat, lon);

            // Course of the leg abeam the aircraft: the bearing to the waypoint is offset by the cross-track angle.
            float bearingToWp = (float)NavMath.Bearing(lat, lon, wp.Latitude, wp.Longitude);
            float legCourse = _distanceM > 1f
                ? Mathf.Repeat(bearingToWp + (Mathf.Asin(Mathf.Clamp(crossTrack / _distanceM, -1f, 1f)) * Mathf.Rad2Deg), 360f)
                : bearingToWp;
            _desiredCourse = legCourse;

            if (DistanceToGo(radius, lat, lon, wp) < CaptureDistance(wp, airSpeed, maxBank))
            {
                _plan.Advance();
                if (!_plan.HasActive)
                {
                    _holdCourse = legCourse;
                }
            }

            float l1;
            float desiredTrack = L1Guidance.DesiredTrack(legCourse, crossTrack, groundSpeed, airSpeed, _gravity, maxBank, out l1);
            _desiredCourse = desiredTrack;

            if (_recorder.Tick(dt))
            {
                _recorder.Record(_distanceM, crossTrack, desiredTrack, vessel.GetTrack(), vessel.GetHeading(), l1,
                    vessel.GetBank(), vessel.GetSideslip() * Mathf.Rad2Deg, _yaw.Output, groundSpeed);
            }

            return desiredTrack;
        }

        private static float HorizontalAirspeed(Vessel vessel)
        {
            Vector3d air = vessel.GetAirVelocity();
            Vector2 horizontal = new Vector2((float)Vector3d.Dot(air, vessel.east), (float)Vector3d.Dot(air, vessel.north));
            return horizontal.magnitude;
        }

        private void UpdateLegStart(double lat, double lon, Waypoint wp, bool engaging)
        {
            if (!engaging && _legIndex == _plan.ActiveIndex && _legDirectTo == _plan.DirectToCount &&
                wp.Latitude == _legLat && wp.Longitude == _legLon)
            {
                return;
            }

            if (!engaging && !_plan.DirectTo && _plan.ActiveIndex > 0)
            {
                Waypoint previous = _plan[_plan.ActiveIndex - 1];
                _fromLat = previous.Latitude;
                _fromLon = previous.Longitude;
            }
            else
            {
                _fromLat = lat;
                _fromLon = lon;
            }

            _legIndex = _plan.ActiveIndex;
            _legDirectTo = _plan.DirectToCount;
            _legLat = wp.Latitude;
            _legLon = wp.Longitude;
        }

        // Along-track distance still to fly to the waypoint; negative once it is behind the aircraft.
        private double DistanceToGo(double radius, double lat, double lon, Waypoint wp)
        {
            float arrival = ArrivalCourse(wp);
            Vector2 fromWp = NavMath.LocalOffset(radius, wp.Latitude, wp.Longitude, lat, lon);
            Vector2 along = new Vector2(Mathf.Sin(arrival * Mathf.Deg2Rad), Mathf.Cos(arrival * Mathf.Deg2Rad));
            return -Vector2.Dot(fromWp, along);
        }

        private float ArrivalCourse(Waypoint wp)
        {
            return Mathf.Repeat((float)NavMath.Bearing(wp.Latitude, wp.Longitude, _fromLat, _fromLon) + 180f, 360f);
        }

        // Start the turn early enough to roll out on the next leg instead of overshooting it.
        private float CaptureDistance(Waypoint wp, float speed, float maxBank)
        {
            int next = _plan.ActiveIndex + 1;
            if (next >= _plan.Count)
            {
                return MinCaptureM;
            }

            Waypoint nextWp = _plan[next];
            float nextCourse = (float)NavMath.Bearing(wp.Latitude, wp.Longitude, nextWp.Latitude, nextWp.Longitude);
            float turn = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(ArrivalCourse(wp), nextCourse)), 120f);
            float turnRadius = (speed * speed) / (_gravity * Mathf.Tan(maxBank * Mathf.Deg2Rad));
            float lead = turnRadius * Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad);
            return Mathf.Max(MinCaptureM, lead);
        }
    }
}
