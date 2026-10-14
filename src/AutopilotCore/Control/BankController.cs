using System;
using UnityEngine;

namespace AutopilotCore
{
    // Turns a heading error into a smooth, bank-limited roll, flown through the fly-by-wire roll-rate loop.
    public sealed class BankController
    {
        public static readonly int[] MaxBankOptions = { 5, 10, 15, 20, 30, 45, 50, 60 };

        private const float BankGain = 1.5f;
        private const float MaxRollRate = 0.35f;
        private const float RateTau = 0.03f;

        private readonly RateLoop _loop = new RateLoop("Bank", 0.4f, 0.5f, 1f, 3f, 0.08f);
        private bool _active;
        private float _bankCmd;
        private float _bankCmdRate;
        private float _rate;
        private float _lastTick = float.NegativeInfinity;

        public void Reset()
        {
            _active = false;
        }

        public void Step(Vessel vessel, FlightCtrlState ctrl, float currentHeading, float targetHeading, float maxBank, float dt)
        {
            float headingError = Mathf.DeltaAngle(currentHeading, targetHeading);
            StepBank(vessel, ctrl, maxBank * (float)Math.Tanh(headingError / 22f), maxBank, dt);
        }

        // bankTarget: degrees, positive = right wing down.
        public void StepBank(Vessel vessel, FlightCtrlState ctrl, float bankTarget, float maxBank, float dt)
        {
            if (!vessel.TryGetBodyRates(out Vector3 body))
            {
                return;
            }

            float bank = vessel.GetBank();
            bool resumed = Time.fixedTime - _lastTick > 0.5f;
            _lastTick = Time.fixedTime;
            if (!_active || resumed)
            {
                _active = true;
                _bankCmd = bank;
                _bankCmdRate = 0f;
                _rate = body.y;
                _loop.Reset(ctrl.roll);
            }

            StepBankCommand(Mathf.Clamp(bankTarget, -maxBank, maxBank), maxBank, dt);
            _rate = Filter.Blend(_rate, body.y, dt, RateTau);

            // p = phiDot - psiDot sin(theta), with the shaped command rate fed forward.
            float pitch = vessel.GetPitch();
            float speed = (float)vessel.GetAirVelocity().magnitude;
            float error = Mathf.DeltaAngle(bank, _bankCmd);
            float phiDot = Mathf.Clamp(((BankGain * error) + _bankCmdRate) * Mathf.Deg2Rad, -MaxRollRate, MaxRollRate);
            float turnRate = Kinematics.LevelTurnRate(bank, speed, vessel.GetGravity());
            float command = phiDot - (turnRate * Mathf.Sin(pitch * Mathf.Deg2Rad));
            ctrl.roll = _loop.Step(command, _rate, vessel.GetDynamicRatio(), 0f, false, dt);
        }

        public static int DrawMaxBankSlider(int index)
        {
            GUILayout.Label("Max bank: " + MaxBankOptions[index] + " deg");
            float slider = GUILayout.HorizontalSlider(index, 0f, MaxBankOptions.Length - 1);
            return Mathf.RoundToInt(slider);
        }

        // Critically damped, then clamped so the roll-in starts from rest and eases out.
        private void StepBankCommand(float bankTarget, float maxBank, float dt)
        {
            const float wn = 0.9f;
            float maxRate = 0.35f * maxBank;
            float maxAccel = 0.18f * maxBank;
            float accel = (wn * wn * (bankTarget - _bankCmd)) - (2f * wn * _bankCmdRate);
            accel = Mathf.Clamp(accel, -maxAccel, maxAccel);
            _bankCmdRate = Mathf.Clamp(_bankCmdRate + (accel * dt), -maxRate, maxRate);
            _bankCmd = Mathf.Clamp(_bankCmd + (_bankCmdRate * dt), -maxBank, maxBank);
        }
    }
}
