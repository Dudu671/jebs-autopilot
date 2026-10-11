using UnityEngine;

namespace AutopilotCore
{
    public static class Filter
    {
        public static float Blend(float current, float sample, float dt, float tau)
        {
            float alpha = 1f - Mathf.Exp(-dt / tau);
            return current + ((sample - current) * alpha);
        }
    }
}
