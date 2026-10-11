using UnityEngine;

namespace AutopilotCore
{
    public static class VesselExtensions
    {
        public static float GetPitch(this Vessel vessel)
        {
            // TODO: validate in flight
            Vector3d forward = vessel.ReferenceTransform.up;
            Vector3d up = vessel.upAxis;
            return (float)(90.0 - Vector3d.Angle(forward, up));
        }

        public static float GetHeading(this Vessel vessel)
        {
            // TODO: validate in flight
            Vector3d up = vessel.upAxis;
            Vector3d north = vessel.north;
            Vector3d east = vessel.east;
            Vector3d forwardHoriz = Vector3d.Exclude(up, vessel.ReferenceTransform.up);
            if (forwardHoriz.sqrMagnitude < 1e-8)
            {
                return 0f;
            }

            forwardHoriz.Normalize();
            double heading = Vector3d.Angle(north, forwardHoriz);
            if (Vector3d.Dot(east, forwardHoriz) < 0.0)
            {
                heading = 360.0 - heading;
            }

            return (float)heading;
        }

        public static float GetAltitudeASL(this Vessel vessel)
        {
            // TODO: validate in flight
            return (float)vessel.altitude;
        }
    }
}
