using UnityEngine;

namespace AutopilotCore
{
    // Cuts a loop's gain when its error keeps reversing faster than a pilot would; recovers slowly.
    public sealed class OscillationGuard
    {
        private const float HalfPeriod = 0.8f;
        private const int Flips = 3;
        private const float Cut = 0.8f;
        private const float MinGain = 0.4f;
        private const float QuietTime = 4f;
        private const float Recover = 0.03f;

        private readonly string _name;
        private readonly float _threshold;
        private int _sign;
        private int _flips;
        private float _clock;
        private float _quiet;

        public OscillationGuard(string name, float threshold)
        {
            _name = name;
            _threshold = threshold;
            Gain = 1f;
        }

        public float Gain { get; private set; }

        public void Reset()
        {
            Gain = 1f;
            _sign = 0;
            _flips = 0;
            _clock = 0f;
            _quiet = 0f;
        }

        public void Step(float error, bool pilotActive, float dt)
        {
            _clock += dt;
            if (pilotActive)
            {
                _sign = 0;
                _flips = 0;
                return;
            }

            int sign = error > _threshold ? 1 : (error < -_threshold ? -1 : 0);
            if (sign != 0 && sign != _sign)
            {
                if (_sign != 0 && _clock < HalfPeriod)
                {
                    _flips++;
                    _quiet = 0f;
                    if (_flips >= Flips)
                    {
                        Gain = Mathf.Max(MinGain, Gain * Cut);
                        _flips = 0;
                        Debug.Log("[AutopilotCore] " + _name + " hunting, gain " + Gain.ToString("F2"));
                    }
                }
                else
                {
                    _flips = 0;
                }

                _sign = sign;
                _clock = 0f;
            }

            _quiet += dt;
            if (_quiet > QuietTime)
            {
                Gain = Mathf.Min(1f, Gain + (Recover * dt));
            }
        }
    }
}
