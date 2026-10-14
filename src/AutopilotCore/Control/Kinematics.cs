using UnityEngine;

namespace AutopilotCore
{
    public static class Kinematics
    {
        private const float MaxCompBank = 60f;
        private const float MinSpeed = 20f;

        // Level turn: psiDot = g tan(phi) / V, with bank capped so a steep bank does not ask for an absurd pull.
        public static float LevelTurnRate(float bankDeg, float speed, float gravity)
        {
            if (speed <= MinSpeed || Mathf.Abs(bankDeg) >= 90f)
            {
                return 0f;
            }

            return gravity * Mathf.Tan(Mathf.Clamp(bankDeg, -MaxCompBank, MaxCompBank) * Mathf.Deg2Rad) / speed;
        }

        // Body pitch rate that keeps the nose where it is in a level turn: q = psiDot sin(phi) cos(theta).
        public static float TurnPitchRate(float bankDeg, float pitchDeg, float speed, float gravity)
        {
            float phi = Mathf.Clamp(bankDeg, -MaxCompBank, MaxCompBank) * Mathf.Deg2Rad;
            return LevelTurnRate(bankDeg, speed, gravity) * Mathf.Sin(phi) * Mathf.Cos(pitchDeg * Mathf.Deg2Rad);
        }
    }
}
