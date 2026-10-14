namespace AutopilotCore
{
    // Autopilot state and control loop of one vessel.
    public sealed class VesselPilot
    {
        private readonly ModuleManager _modules = new ModuleManager();
        private readonly FlyByWire _flyByWire = new FlyByWire();
        private readonly PitchHold _pitch;
        private readonly AltitudeHold _altitude;
        private readonly HeadingHold _heading;
        private readonly NavHold _nav;
        private Vessel _vessel;

        public VesselPilot(Vessel vessel)
        {
            _pitch = _modules.Find<PitchHold>();
            _altitude = _modules.Find<AltitudeHold>();
            _heading = _modules.Find<HeadingHold>();
            _nav = _modules.Find<NavHold>();
            _vessel = vessel;
            _vessel.OnFlyByWire += Fly;
        }

        public Vessel Vessel
        {
            get { return _vessel; }
        }

        public ModuleManager Modules
        {
            get { return _modules; }
        }

        public FlyByWire FlyByWire
        {
            get { return _flyByWire; }
        }

        public NavHold Nav
        {
            get { return _nav; }
        }

        public void Detach()
        {
            if (_vessel != null)
            {
                _vessel.OnFlyByWire -= Fly;
                _vessel = null;
            }
        }

        private void Fly(FlightCtrlState ctrl)
        {
            if (_vessel == null)
            {
                return;
            }

            bool pitchOwned = (_pitch != null && _pitch.Enabled) || (_altitude != null && _altitude.Enabled);
            bool rollOwned = (_heading != null && _heading.Enabled) || (_nav != null && _nav.Enabled);

            // Off the active vessel ctrl still holds last frame's output, not pilot input.
            _flyByWire.Apply(_vessel, ctrl, _vessel.isActiveVessel, pitchOwned, rollOwned);
            _modules.UpdateAll(_vessel, ctrl);
            _flyByWire.ReassertYaw(ctrl);
        }
    }
}
