namespace AutopilotCore
{
    // Autopilot state and control loop of one vessel.
    public sealed class VesselPilot
    {
        private readonly ModuleManager _modules = new ModuleManager();
        private readonly FlyByWire _flyByWire = new FlyByWire();
        private readonly NavHold _nav;
        private readonly Autoland _autoland;
        private Vessel _vessel;

        public VesselPilot(Vessel vessel)
        {
            _nav = _modules.Find<NavHold>();
            _autoland = _modules.Find<Autoland>();
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

            bool pitchOwned = _modules.AnyEnabled<IVerticalMode>();
            bool rollOwned = _modules.AnyEnabled<ILateralMode>();

            // Off the active vessel ctrl still holds last frame's output, not pilot input.
            _flyByWire.Apply(_vessel, ctrl, _vessel.isActiveVessel, pitchOwned, rollOwned);
            _modules.UpdateAll(_vessel, ctrl);
            if (_autoland == null || !_autoland.OwnsRudder)
            {
                _flyByWire.ReassertYaw(ctrl);
            }
        }
    }
}
