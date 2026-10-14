using UnityEngine;

namespace AutopilotCore
{
    // Body-rate PI with feedforward, gains scheduled by dynamic pressure: P and I with 1/q,
    // feedforward with 1/sqrt(q). Gains are stick per rad/s at the reference dynamic pressure,
    // scaled by the surface effectiveness learned in flight.
    public sealed class RateLoop
    {
        private const float MinQ = 0.3f;
        private const float MaxQ = 4f;
        private const float IntegralLimit = 1f;

        // Effectiveness estimate: slope of filtered rate on filtered stick over a sliding window.
        private const float SignalTau = 0.3f;
        private const float WindowTau = 8f;
        private const float LearnTau = 3f;
        private const float MinExcitation = 0.01f;
        private const float MinScale = 0.33f;
        private const float MaxScale = 10f;

        private readonly float _feed;
        private readonly float _kp;
        private readonly float _ki;
        private readonly float _slew;
        private readonly float _prior;
        private readonly OscillationGuard _guard;

        private float _effect;
        private bool _seeded;
        private float _stickF;
        private float _rateF;
        private float _meanStick;
        private float _meanRate;
        private float _varStick;
        private float _covar;

        public RateLoop(string name, float feed, float kp, float ki, float slew, float guardThreshold)
        {
            _feed = feed;
            _kp = kp;
            _ki = ki;
            _slew = slew;
            _prior = 1f / feed;
            _effect = _prior;
            _guard = new OscillationGuard(name, guardThreshold);
        }

        public float Output { get; private set; }

        public float Integral { get; private set; }

        public float Gain
        {
            get { return _guard.Gain; }
        }

        // rad/s per stick at the reference dynamic pressure.
        public float Effectiveness
        {
            get { return _effect; }
        }

        // Bumpless: starts from the surface position already applied. The learned effectiveness is kept.
        public void Reset(float output)
        {
            Output = output;
            Integral = Mathf.Clamp(output, -IntegralLimit, IntegralLimit);
            _seeded = false;
            _guard.Reset();
        }

        // Another controller drives the surface; the integral stays as the trim it held.
        public void Hold()
        {
            Output = Mathf.Clamp(Output, -1f, 1f);
        }

        public float Step(float command, float rate, float qRatio, float extraFeed, bool pilotActive, float dt)
        {
            float q = Mathf.Clamp(qRatio, MinQ, MaxQ);
            float rootQ = Mathf.Sqrt(q);
            float scale = Mathf.Clamp(_prior / _effect, MinScale, MaxScale);
            float error = command - rate;
            float gain = _guard.Gain / q;

            // Full scale on the static terms; the proportional term gets the square root, since a very
            // stable airframe loses steady rate per stick but not its initial pitch acceleration.
            float feed = (_feed * scale * command / rootQ) + extraFeed;
            float unclamped = feed + (_kp * Mathf.Sqrt(scale) * gain * error) + Integral;
            float wanted = Mathf.Clamp(unclamped, -1f, 1f);
            float stick = Mathf.MoveTowards(Output, wanted, _slew * dt);

            // Anti-windup at full deflection or while slewing toward the error.
            bool atLimit = Mathf.Abs(unclamped) >= 1f && Mathf.Sign(unclamped) == Mathf.Sign(error);
            bool slewing = Mathf.Abs(wanted - stick) > 1e-4f && Mathf.Sign(wanted - stick) == Mathf.Sign(error);
            if (!atLimit && !slewing)
            {
                Integral = Mathf.Clamp(Integral + (_ki * scale * gain * error * dt), -IntegralLimit, IntegralLimit);
            }

            Output = stick;
            _guard.Step(error, pilotActive, dt);
            Learn(stick, rate / rootQ, dt);
            return stick;
        }

        private void Learn(float stick, float rate, float dt)
        {
            if (!_seeded)
            {
                _stickF = stick;
                _rateF = rate;
                _meanStick = stick;
                _meanRate = rate;
                _varStick = 0f;
                _covar = 0f;
                _seeded = true;
                return;
            }

            _stickF = Filter.Blend(_stickF, stick, dt, SignalTau);
            _rateF = Filter.Blend(_rateF, rate, dt, SignalTau);
            float window = 1f - Mathf.Exp(-dt / WindowTau);
            _meanStick += (_stickF - _meanStick) * window;
            _meanRate += (_rateF - _meanRate) * window;
            float dStick = _stickF - _meanStick;
            _varStick += ((dStick * dStick) - _varStick) * window;
            _covar += ((dStick * (_rateF - _meanRate)) - _covar) * window;

            if (_varStick < MinExcitation)
            {
                return;
            }

            float observed = _covar / _varStick;
            if (observed <= 0f)
            {
                return;
            }

            _effect = Mathf.Clamp(Filter.Blend(_effect, observed, dt, LearnTau), _prior / MaxScale, _prior / MinScale);
        }
    }
}
