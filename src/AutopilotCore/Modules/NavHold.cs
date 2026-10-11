using System;
using UnityEngine;

namespace AutopilotCore
{
    // Follows the waypoint legs with L1 guidance: aim at a point L1 meters ahead on the leg and
    // command the bank for the lateral acceleration that reaches it. Commanding bank directly
    // avoids stacking a cross-track loop on the heading loop, which made the aircraft weave.
    public sealed class NavHold : ILateralMode
    {
        private const float L1Period = 25f;
        private const float L1Damping = 0.75f;
        private const float MinL1 = 300f;
        private const float MinCaptureM = 150f;
        private const float MinGuidanceSpeed = 10f;
        private const float Gravity = 9.81f;
        // Heading error (rad) at which the guidance reaches max bank.
        private const float LinearEta = 0.5f;

        private readonly BankController _bank = new BankController();
        private readonly FlightRecorder _recorder = new FlightRecorder(
            "nav_hold.csv",
            "t,dist_m,xtrack_m,leg_course,track,nu1,nu2,l1_m,bank_target,bank,gs_mps");
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
                return;
            }

            bool engaging = !_active;
            _active = true;
            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float maxBank = BankController.MaxBankOptions[_maxBankIndex];
            Vector2 groundVel = vessel.GetGroundVelocity();
            float current = groundVel.magnitude > MinGuidanceSpeed ? vessel.GetTrack() : vessel.GetHeading();
            if (engaging)
            {
                _holdCourse = current;
                _recorder.Start();
            }

            if (_plan.HasActive && groundVel.magnitude > MinGuidanceSpeed)
            {
                float bankTarget = GuideToActive(vessel, groundVel, maxBank, engaging);
                ctrl.roll = _bank.StepBank(vessel, bankTarget, maxBank, dt);
                return;
            }

            _desiredCourse = _plan.HasActive
                ? (float)NavMath.Bearing(vessel.latitude, vessel.longitude, _plan.Active.Latitude, _plan.Active.Longitude)
                : _holdCourse;
            ctrl.roll = _bank.Step(vessel, current, _desiredCourse, maxBank, dt);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            if (_plan.Count == 0)
            {
                GUILayout.Label("No waypoints. Add them on the Navigation Display.");
            }
            else if (!_plan.HasActive)
            {
                GUILayout.Label("Plan complete, holding " + Format.Heading(_holdCourse));
            }
            else if (Enabled)
            {
                GUILayout.Label("To " + NavPlan.NameOf(_plan.ActiveIndex) + "   " + (_distanceM / 1000f).ToString("F1") +
                                " km   CRS " + Format.Heading(_desiredCourse));
            }
            else
            {
                GUILayout.Label("Next: " + NavPlan.NameOf(_plan.ActiveIndex));
            }

            _maxBankIndex = BankController.DrawMaxBankSlider(_maxBankIndex);
        }

        private float GuideToActive(Vessel vessel, Vector2 groundVel, float maxBank, bool engaging)
        {
            double radius = vessel.mainBody.Radius;
            double lat = vessel.latitude;
            double lon = vessel.longitude;
            Waypoint wp = _plan.Active;
            UpdateLegStart(lat, lon, wp, engaging);

            float speed = groundVel.magnitude;
            _distanceM = (float)NavMath.Distance(radius, lat, lon, wp.Latitude, wp.Longitude);
            float crossTrack = (float)NavMath.CrossTrack(radius, _fromLat, _fromLon, wp.Latitude, wp.Longitude, lat, lon);

            // Course of the leg abeam the aircraft: the bearing to the waypoint is offset by the cross-track angle.
            float bearingToWp = (float)NavMath.Bearing(lat, lon, wp.Latitude, wp.Longitude);
            float legCourse = _distanceM > 1f
                ? Mathf.Repeat(bearingToWp + (Mathf.Asin(Mathf.Clamp(crossTrack / _distanceM, -1f, 1f)) * Mathf.Rad2Deg), 360f)
                : bearingToWp;
            _desiredCourse = legCourse;

            if (DistanceToGo(radius, lat, lon, wp) < CaptureDistance(wp, speed, maxBank))
            {
                _plan.Advance();
            }

            float courseRad = legCourse * Mathf.Deg2Rad;
            Vector2 along = new Vector2(Mathf.Sin(courseRad), Mathf.Cos(courseRad));
            Vector2 right = new Vector2(along.y, -along.x);

            // L1 must scale with the turn radius: shorter than that, small errors already demand more
            // than max bank and the command saturates left and right in a limit cycle.
            float gain = 4f * L1Damping * L1Damping;
            float maxLateralAccel = Gravity * Mathf.Tan(maxBank * Mathf.Deg2Rad);
            float l1ForBank = gain * speed * speed * Mathf.Sin(LinearEta) / maxLateralAccel;
            float l1 = Mathf.Max(MinL1, Mathf.Max(L1Damping * L1Period * speed / Mathf.PI, l1ForBank));

            float nu1 = Mathf.Asin(Mathf.Clamp(crossTrack / l1, -0.7071f, 0.7071f));
            float nu2 = Mathf.Atan2(Vector2.Dot(groundVel, right), Vector2.Dot(groundVel, along));
            float eta = Mathf.Clamp(-(nu1 + nu2), -Mathf.PI * 0.5f, Mathf.PI * 0.5f);

            float lateralAccel = gain * speed * speed / l1 * Mathf.Sin(eta);
            float bankTarget = Mathf.Atan(lateralAccel / Gravity) * Mathf.Rad2Deg;

            if (_recorder.Tick(Time.fixedDeltaTime))
            {
                _recorder.Record(_distanceM, crossTrack, legCourse, vessel.GetTrack(), nu1 * Mathf.Rad2Deg,
                    nu2 * Mathf.Rad2Deg, l1, bankTarget, vessel.GetBank(), speed);
            }

            return bankTarget;
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
            float turnRadius = (speed * speed) / (Gravity * Mathf.Tan(maxBank * Mathf.Deg2Rad));
            float lead = turnRadius * Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad);
            return Mathf.Max(MinCaptureM, lead);
        }
    }
}
