using System;
using UnityEngine;

namespace AutopilotCore
{
    public sealed class HeadingHold : IAutopilotModule
    {
        private const float DesignIasKt = 110f;
        private const float DesignRollAuthority = 28f;

        private static readonly int[] MaxBankOptions = { 5, 10, 15, 20, 30, 45, 50, 60 };

        private readonly FloatField _target = new FloatField(0f);
        private int _maxBankIndex = 4;
        private bool _active;
        private float _bankCmd;
        private float _bankCmdRate;
        private float _bankFilt;
        private float _lastBank;
        private float _rollRate;
        private float _integral;
        private float _stick;
        private float _authority = DesignRollAuthority;

        public string Name
        {
            get { return "Heading Hold"; }
        }

        public bool Enabled { get; set; }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _active = false;
                return;
            }

            float dt = Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.01f, 0.05f);
            float bank = vessel.GetBank();
            if (!_active)
            {
                Engage(bank);
            }

            float maxBank = MaxBankOptions[_maxBankIndex];
            float headingError = Mathf.DeltaAngle(vessel.GetHeading(), _target.Value);
            float bankTarget = maxBank * (float)Math.Tanh(headingError / 22f);
            StepBankCommand(bankTarget, maxBank, dt);

            _bankFilt = Blend(_bankFilt, bank, dt, 0.1f);
            float rawRate = (bank - _lastBank) / dt;
            _lastBank = bank;
            _rollRate = Blend(_rollRate, rawRate, dt, 0.15f);

            float bankError = _bankCmd - _bankFilt;
            float maxRate = 0.45f * maxBank;
            float rateCmd = Mathf.Clamp(0.85f * bankError, -maxRate, maxRate);
            float rateError = rateCmd - _rollRate;
            if (Mathf.Abs(rateError) < 0.7f)
            {
                rateError = 0f;
            }

            LearnAuthority(dt);
            float scale = StickScale(vessel.GetIasKt()) * Mathf.Clamp(DesignRollAuthority / _authority, 0.3f, 2.2f);

            _integral = Mathf.Clamp(_integral + bankError * dt, -6f, 6f);
            float stickCmd = scale * ((0.022f * rateError) + (0.0012f * _integral));
            _stick = Blend(_stick, Mathf.Clamp(stickCmd, -1f, 1f), dt, 0.12f);
            ctrl.roll = _stick;
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            _target.Draw("Target (deg)");
            GUILayout.Label("Max bank: " + MaxBankOptions[_maxBankIndex] + " deg");
            float slider = GUILayout.HorizontalSlider(_maxBankIndex, 0f, MaxBankOptions.Length - 1);
            _maxBankIndex = Mathf.RoundToInt(slider);
        }

        private void Engage(float bank)
        {
            _active = true;
            _bankCmd = bank;
            _bankCmdRate = 0f;
            _bankFilt = bank;
            _lastBank = bank;
            _rollRate = 0f;
            _integral = 0f;
            _stick = 0f;
        }

        private void StepBankCommand(float bankTarget, float maxBank, float dt)
        {
            // Critically damped, then clamped so the roll-in starts from rest and eases out.
            const float wn = 0.9f;
            float maxRate = 0.35f * maxBank;
            float maxAccel = 0.18f * maxBank;
            float accel = (wn * wn * (bankTarget - _bankCmd)) - (2f * wn * _bankCmdRate);
            accel = Mathf.Clamp(accel, -maxAccel, maxAccel);
            _bankCmdRate = Mathf.Clamp(_bankCmdRate + (accel * dt), -maxRate, maxRate);
            _bankCmd = Mathf.Clamp(_bankCmd + (_bankCmdRate * dt), -maxBank, maxBank);
        }

        private void LearnAuthority(float dt)
        {
            if (Mathf.Abs(_stick) < 0.12f || Mathf.Sign(_rollRate) != Mathf.Sign(_stick))
            {
                return;
            }

            float observed = Mathf.Clamp(Mathf.Abs(_rollRate / _stick), 6f, 100f);
            _authority = Blend(_authority, observed, dt, 1.5f);
        }

        private static float StickScale(float iasKt)
        {
            float ias = Mathf.Max(iasKt, 45f);
            return Mathf.Clamp(DesignIasKt / ias, 0.35f, 2f);
        }

        private static float Blend(float current, float sample, float dt, float tau)
        {
            float alpha = 1f - Mathf.Exp(-dt / tau);
            return current + ((sample - current) * alpha);
        }
    }
}
