using System;
using UnityEngine;

namespace AutopilotCore
{
    // Turns a heading error into a smooth, bank-limited roll command.
    public sealed class BankController
    {
        public static readonly int[] MaxBankOptions = { 5, 10, 15, 20, 30, 45, 50, 60 };

        private const float DesignIasKt = 110f;
        private const float DesignRollAuthority = 28f;

        private bool _active;
        private float _bankCmd;
        private float _bankCmdRate;
        private float _bankFilt;
        private float _lastBank;
        private float _rollRate;
        private float _integral;
        private float _stick;
        private float _authority = DesignRollAuthority;

        public void Reset()
        {
            _active = false;
        }

        public float Step(Vessel vessel, float currentHeading, float targetHeading, float maxBank, float dt)
        {
            float headingError = Mathf.DeltaAngle(currentHeading, targetHeading);
            return StepBank(vessel, maxBank * (float)Math.Tanh(headingError / 22f), maxBank, dt);
        }

        // bankTarget: degrees, positive = right wing down.
        public float StepBank(Vessel vessel, float bankTarget, float maxBank, float dt)
        {
            float bank = vessel.GetBank();
            if (!_active)
            {
                Engage(bank);
            }

            StepBankCommand(Mathf.Clamp(bankTarget, -maxBank, maxBank), maxBank, dt);

            _bankFilt = Filter.Blend(_bankFilt, bank, dt, 0.1f);
            float rawRate = (bank - _lastBank) / dt;
            _lastBank = bank;
            _rollRate = Filter.Blend(_rollRate, rawRate, dt, 0.15f);

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

            _integral = Mathf.Clamp(_integral + (bankError * dt), -6f, 6f);
            float stickCmd = scale * ((0.022f * rateError) + (0.0012f * _integral));
            _stick = Filter.Blend(_stick, Mathf.Clamp(stickCmd, -1f, 1f), dt, 0.12f);
            return _stick;
        }

        public static int DrawMaxBankSlider(int index)
        {
            GUILayout.Label("Max bank: " + MaxBankOptions[index] + " deg");
            float slider = GUILayout.HorizontalSlider(index, 0f, MaxBankOptions.Length - 1);
            return Mathf.RoundToInt(slider);
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
            _authority = Filter.Blend(_authority, observed, dt, 1.5f);
        }

        private static float StickScale(float iasKt)
        {
            float ias = Mathf.Max(iasKt, 45f);
            return Mathf.Clamp(DesignIasKt / ias, 0.35f, 2f);
        }
    }
}
