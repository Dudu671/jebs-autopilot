using UnityEngine;

namespace AutopilotCore
{
    // Load factor and AoA limits on a pitch rate command (rad/s). Load factor n = cos(theta) cos(phi) + V q / g.
    // The AoA limit only bites near the stall; past it the allowed rate turns negative and pushes the nose back.
    public static class Envelope
    {
        public const float DefaultMaxLoad = 7f;
        public const float DefaultMinLoad = -3f;

        private const float MaxAoa = 20f;
        private const float MinAoa = -12f;
        private const float AoaPush = 4f;
        private const float MaxRate = 1f;
        private const float MinSpeed = 20f;

        public static float MaxPitchRate(float speed, float gravity, float pitchDeg, float bankDeg, float maxLoad)
        {
            return Mathf.Clamp((maxLoad - Level(pitchDeg, bankDeg)) * gravity / Mathf.Max(speed, MinSpeed), 0f, MaxRate);
        }

        public static float MinPitchRate(float speed, float gravity, float pitchDeg, float bankDeg, float minLoad)
        {
            return -Mathf.Clamp((Level(pitchDeg, bankDeg) - minLoad) * gravity / Mathf.Max(speed, MinSpeed), 0f, MaxRate);
        }

        // Without a load limit only the rate cap and the AoA limit apply.
        public static float Limit(float pitchRate, float aoaDeg, float speed, float gravity, float pitchDeg, float bankDeg,
            bool loadLimited, float maxLoad, float minLoad)
        {
            float upper = loadLimited ? MaxPitchRate(speed, gravity, pitchDeg, bankDeg, maxLoad) : MaxRate;
            float lower = loadLimited ? MinPitchRate(speed, gravity, pitchDeg, bankDeg, minLoad) : -MaxRate;
            upper = Mathf.Min(upper, AoaPush * (MaxAoa - aoaDeg) * Mathf.Deg2Rad);
            lower = Mathf.Max(lower, AoaPush * (MinAoa - aoaDeg) * Mathf.Deg2Rad);
            if (lower > upper)
            {
                return 0.5f * (lower + upper);
            }

            return Mathf.Clamp(pitchRate, lower, upper);
        }

        public static float RateCap
        {
            get { return MaxRate; }
        }

        private static float Level(float pitchDeg, float bankDeg)
        {
            return Mathf.Cos(pitchDeg * Mathf.Deg2Rad) * Mathf.Cos(bankDeg * Mathf.Deg2Rad);
        }
    }
}
