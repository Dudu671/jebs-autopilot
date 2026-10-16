namespace AutopilotCore
{
    public sealed class FlightData
    {
        public bool Valid;
        public CelestialBody Body;
        public double Latitude;
        public double Longitude;

        public float Pitch;
        public float Bank;
        public float Heading;
        public float Track;
        public float AoA;
        public float FpvUp;
        public float FpvRight;

        public float IasKt;
        public float TasKt;
        public float GsKt;
        public float Mach;
        public float IasTrendKt;
        public float AltFt;
        public float VsFpm;
        public float OatC;

        public bool HasSpeedTarget;
        public bool SpeedOn;
        public float SpeedTargetKt;
        public bool HasHeadingTarget;
        public bool HeadingOn;
        public float HeadingTarget;
        public bool HasAltitudeTarget;
        public bool AltitudeOn;
        public float AltitudeTargetFt;
        public bool PitchOn;
        public float PitchTarget;
        public bool NavOn;
        public string NavLabel;
        public bool HasNavTrack;
        public float NavTrack;
        public bool HoldOn;
        public string HoldLabel;
        public bool LandOn;
        public string LandRunway;
        public string LandLateral;
        public string LandVertical;
        public bool HasOrbit;
        public double OrbitLat;
        public double OrbitLon;
        public float OrbitRadiusM;

        private bool _hasLastIas;
        private float _lastIas;
        private float _iasAccel;

        public void Update(Vessel vessel, ModuleManager modules, float dt)
        {
            Valid = vessel != null && vessel.ReferenceTransform != null;
            if (!Valid)
            {
                _hasLastIas = false;
                return;
            }

            Body = vessel.mainBody;
            Latitude = vessel.latitude;
            Longitude = vessel.longitude;
            Pitch = vessel.GetPitch();
            Bank = vessel.GetBank();
            Heading = vessel.GetHeading();
            Track = vessel.GetTrack();
            AoA = vessel.GetAoA();
            vessel.GetFlightPathOffsets(out FpvUp, out FpvRight);

            IasKt = vessel.GetIasKt();
            TasKt = vessel.GetTasKt();
            GsKt = vessel.GetGsKt();
            Mach = vessel.GetMach();
            AltFt = vessel.GetAltitudeAslFt();
            VsFpm = vessel.GetVerticalSpeedFpm();
            OatC = vessel.GetOatC();

            if (_hasLastIas && dt > 1e-4f)
            {
                _iasAccel = Filter.Blend(_iasAccel, (IasKt - _lastIas) / dt, dt, 1f);
            }

            _lastIas = IasKt;
            _hasLastIas = true;
            IasTrendKt = _iasAccel * 10f;

            SpeedHold speed = modules.Find<SpeedHold>();
            HasSpeedTarget = speed != null;
            SpeedOn = speed != null && speed.Enabled;
            SpeedTargetKt = speed != null ? speed.TargetKt : 0f;

            HeadingHold heading = modules.Find<HeadingHold>();
            HasHeadingTarget = heading != null;
            HeadingOn = heading != null && heading.Enabled;
            HeadingTarget = heading != null ? heading.TargetHeading : 0f;

            AltitudeHold altitude = modules.Find<AltitudeHold>();
            HasAltitudeTarget = altitude != null;
            AltitudeOn = altitude != null && altitude.Enabled;
            AltitudeTargetFt = altitude != null ? altitude.TargetFt : 0f;

            PitchHold pitch = modules.Find<PitchHold>();
            PitchOn = pitch != null && pitch.Enabled;
            PitchTarget = pitch != null ? pitch.TargetPitch : 0f;

            NavHold nav = modules.Find<NavHold>();
            NavOn = nav != null && nav.Enabled;
            NavLabel = nav != null ? nav.ActiveLabel : "";
            float navTrack = 0f;
            HasNavTrack = nav != null && nav.TryGetDesiredTrack(out navTrack);

            HoldPattern hold = modules.Find<HoldPattern>();
            HoldOn = hold != null && hold.Enabled;
            HoldLabel = hold != null ? hold.Label : "";
            HasOrbit = hold != null && hold.TryGetOrbit(out OrbitLat, out OrbitLon, out OrbitRadiusM);
            float holdTrack;
            if (hold != null && hold.TryGetDesiredTrack(out holdTrack))
            {
                HasNavTrack = true;
                navTrack = holdTrack;
            }

            Autoland land = modules.Find<Autoland>();
            LandOn = land != null && land.Enabled;
            if (LandOn)
            {
                LandRunway = land.RunwayName;
                LandLateral = land.LateralLabel;
                LandVertical = land.VerticalLabel;
                SpeedOn = true;
                SpeedTargetKt = land.SpeedTargetKt;
                float landTrack;
                if (land.TryGetDesiredTrack(out landTrack))
                {
                    HasNavTrack = true;
                    navTrack = landTrack;
                }
            }

            NavTrack = navTrack;
        }
    }
}
