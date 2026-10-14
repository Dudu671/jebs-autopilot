using UnityEngine;

namespace AutopilotCore
{
    // Orbits a ground point. On engage the center is placed one radius abeam, so the orbit
    // starts tangent to the current track.
    public sealed class HoldPattern : ILateralMode
    {
        private const float MinGuidanceSpeed = 10f;
        private const float MinRadiusM = 100f;
        private const float MinLookahead = 300f;
        private const int Left = 0;
        private const int Right = 1;

        private static readonly string[] Directions = { "Left", "Right" };

        private readonly BankController _bank = new BankController();
        private readonly YawControl _yaw = new YawControl("Hold yaw");
        private readonly FloatField _radiusKm = new FloatField(3f);
        private int _maxBankIndex = 4;
        private int _direction = Right;

        private bool _active;
        private int _engagedDirection;
        private double _centerLat;
        private double _centerLon;
        private float _radiusM;
        private float _desiredTrack;
        private float _distanceM;
        private float _minTurnRadiusM;

        public string Name
        {
            get { return "Hold Pattern"; }
        }

        public bool Enabled { get; set; }

        public string Label
        {
            get { return (_direction == Right ? "R " : "L ") + _radiusKm.Value.ToString("0.#"); }
        }

        public bool TryGetDesiredTrack(out float track)
        {
            track = _desiredTrack;
            return _active;
        }

        public bool TryGetOrbit(out double latitude, out double longitude, out float radiusM)
        {
            latitude = _centerLat;
            longitude = _centerLon;
            radiusM = _radiusM;
            return _active;
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null || vessel.mainBody == null)
            {
                _active = false;
                _bank.Reset();
                _yaw.Release();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float maxBank = BankController.MaxBankOptions[_maxBankIndex];
            float sign = _direction == Right ? 1f : -1f;
            double bodyRadius = vessel.mainBody.Radius;
            float groundSpeed = vessel.GetGroundVelocity().magnitude;
            float gravity = Mathf.Max(vessel.GetGravity(), 0.1f);
            float track = groundSpeed > MinGuidanceSpeed ? vessel.GetTrack() : vessel.GetHeading();
            _radiusM = Mathf.Max(_radiusKm.Value * 1000f, MinRadiusM);

            if (!_active || _engagedDirection != _direction)
            {
                PlaceCenter(vessel, bodyRadius, track, sign);
                _active = true;
                _engagedDirection = _direction;
            }

            Vector2 fromCenter = NavMath.LocalOffset(bodyRadius, _centerLat, _centerLon, vessel.latitude, vessel.longitude);
            _distanceM = fromCenter.magnitude;
            float radial = Mathf.Atan2(fromCenter.x, fromCenter.y) * Mathf.Rad2Deg;
            float lookahead = Mathf.Max(MinLookahead, _radiusM * 0.5f);
            float radialError = _distanceM - _radiusM;

            // Tangent to the circle, turned toward it by the radial error.
            float approach = Mathf.Atan(radialError / lookahead) * Mathf.Rad2Deg;
            _desiredTrack = Mathf.Repeat(radial + (sign * (90f + approach)), 360f);

            // Steady-turn bank for this radius, faded out while still far from the circle.
            float onCircle = Mathf.Clamp01(1f - (Mathf.Abs(radialError) / (2f * lookahead)));
            float turnBank = sign * Mathf.Atan(groundSpeed * groundSpeed / (gravity * _radiusM)) * Mathf.Rad2Deg;
            float trackError = Mathf.DeltaAngle(track, _desiredTrack);
            float bankTarget = (onCircle * turnBank) + (maxBank * Tanh(trackError / 22f));
            _minTurnRadiusM = groundSpeed * groundSpeed / (gravity * Mathf.Tan(maxBank * Mathf.Deg2Rad));

            _bank.StepBank(vessel, ctrl, bankTarget, maxBank, dt);
            _yaw.Coordinate(vessel, ctrl, dt);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Turn", GUILayout.Width(110f));
            _direction = GUILayout.Toolbar(_direction, Directions, GuiStyles.ToggleButton);
            GUILayout.EndHorizontal();
            _radiusKm.Draw("Radius (km)");
            _maxBankIndex = BankController.DrawMaxBankSlider(_maxBankIndex);
            if (_active)
            {
                GUILayout.Label("From center " + (_distanceM / 1000f).ToString("F1") + " km   min radius " +
                                (_minTurnRadiusM / 1000f).ToString("F1") + " km");
            }
        }

        private void PlaceCenter(Vessel vessel, double bodyRadius, float track, float sign)
        {
            float bearing = (track + (sign * 90f)) * Mathf.Deg2Rad;
            NavMath.FromLocal(bodyRadius, vessel.latitude, vessel.longitude,
                _radiusM * Mathf.Sin(bearing), _radiusM * Mathf.Cos(bearing), out _centerLat, out _centerLon);
        }

        private static float Tanh(float x)
        {
            return (float)System.Math.Tanh(x);
        }
    }
}
