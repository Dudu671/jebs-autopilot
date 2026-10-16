using UnityEngine;

namespace AutopilotCore
{
    public sealed class HeadingHold : ILateralMode
    {
        private readonly BankController _bank = new BankController();
        private readonly YawControl _yaw = new YawControl("Heading yaw");
        private readonly FloatField _target = new FloatField(0f);
        private int _maxBankIndex = 4;

        public string Name
        {
            get { return "Heading Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetHeading
        {
            get { return Mathf.Repeat(_target.Value, 360f); }
            set { _target.Value = Mathf.Round(Mathf.Repeat(value, 360f)); }
        }

        public string Summary
        {
            get { return "HDG " + Format.Heading(TargetHeading); }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _bank.Reset();
                _yaw.Release();
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float maxBank = BankController.MaxBankOptions[_maxBankIndex];
            _bank.Step(vessel, ctrl, vessel.GetHeading(), TargetHeading, maxBank, dt);
            _yaw.Coordinate(vessel, ctrl, dt);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name, GuiStyles.Toggle);
            _target.Draw("Target (deg)");
            _maxBankIndex = BankController.DrawMaxBankSlider(_maxBankIndex);
        }
    }
}
