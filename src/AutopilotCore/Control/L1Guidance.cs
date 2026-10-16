using UnityEngine;

namespace AutopilotCore
{
    // Ground track toward a point L1 meters ahead on a line, so the track converges on it.
    public static class L1Guidance
    {
        private const float Period = 25f;
        private const float Damping = 0.75f;
        private const float MinL1 = 300f;

        // Heading error (rad) at which the guidance reaches max bank.
        private const float LinearEta = 0.5f;

        // crossTrack: meters, positive right of the course.
        public static float DesiredTrack(float course, float crossTrack, float groundSpeed, float airSpeed, float gravity,
            float maxBank, out float l1)
        {
            float courseRad = course * Mathf.Deg2Rad;
            Vector2 along = new Vector2(Mathf.Sin(courseRad), Mathf.Cos(courseRad));
            Vector2 right = new Vector2(along.y, -along.x);

            // L1 must scale with the turn radius: shorter than that, small errors already demand more
            // than max bank and the command saturates left and right in a limit cycle.
            float gain = 4f * Damping * Damping;
            float maxLateralAccel = gravity * Mathf.Tan(maxBank * Mathf.Deg2Rad);
            float l1ForBank = gain * airSpeed * airSpeed * Mathf.Sin(LinearEta) / maxLateralAccel;
            l1 = Mathf.Max(MinL1, Mathf.Max(Damping * Period * groundSpeed / Mathf.PI, l1ForBank));
            float offset = Mathf.Clamp(crossTrack, -0.9f * l1, 0.9f * l1);
            float ahead = Mathf.Sqrt((l1 * l1) - (offset * offset));
            Vector2 toPoint = (along * ahead) - (right * offset);
            return Mathf.Repeat(Mathf.Atan2(toPoint.x, toPoint.y) * Mathf.Rad2Deg, 360f);
        }
    }
}
