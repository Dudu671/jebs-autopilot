using UnityEngine;

namespace AutopilotCore
{
    public sealed class HeadingHold : ILateralMode
    {
        private readonly BankController _bank = new BankController();
        private readonly FloatField _target = new FloatField(0f);
        private int _maxBankIndex = 4;

        public string Name
        {
            get { return "Heading Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetHeading
        {
            get { return _target.Value; }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _bank.Reset();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float maxBank = BankController.MaxBankOptions[_maxBankIndex];
            ctrl.roll = _bank.Step(vessel, vessel.GetHeading(), _target.Value, maxBank, dt);
            ctrl.yaw = vessel.GetCoordinationYaw();
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            _target.Draw("Target (deg)");
            _maxBankIndex = BankController.DrawMaxBankSlider(_maxBankIndex);
        }
    }
}
