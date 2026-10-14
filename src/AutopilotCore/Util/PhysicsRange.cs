using UnityEngine;

namespace AutopilotCore
{
    // Keeps flying vessels in physics up to 100 km from the active one. Never shrinks ranges
    // another mod (Physics Range Extender) already raised.
    public static class PhysicsRange
    {
        private const float Unpack = 99000f;
        private const float Load = 100000f;
        private const float Pack = 102000f;
        private const float Unload = 105000f;

        public static void Extend(Vessel vessel)
        {
            if (vessel == null || vessel.vesselRanges == null)
            {
                return;
            }

            VesselRanges.Situation flying = vessel.vesselRanges.flying;
            if (flying == null)
            {
                return;
            }

            flying.load = Mathf.Max(flying.load, Load);
            flying.unload = Mathf.Max(flying.unload, Unload, flying.load + 1000f);
            flying.unpack = Mathf.Max(flying.unpack, Unpack);
            flying.pack = Mathf.Max(flying.pack, Pack, flying.unpack + 1000f);
        }
    }
}
