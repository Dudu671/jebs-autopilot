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

        public static float GetBank(this Vessel vessel)
        {
            // TODO: validate in flight
            Vector3d up = vessel.upAxis;
            Vector3d right = vessel.ReferenceTransform.right;
            Vector3d dorsal = -vessel.ReferenceTransform.forward;
            double roll = System.Math.Atan2(Vector3d.Dot(right, up), Vector3d.Dot(dorsal, up));
            return (float)(-roll * (180.0 / System.Math.PI));
        }

        public static float GetTrack(this Vessel vessel)
        {
            // TODO: validate in flight
            Vector3d horiz = Vector3d.Exclude(vessel.upAxis, vessel.GetSrfVelocity());
            if (horiz.sqrMagnitude < 0.25)
            {
                return vessel.GetHeading();
            }

            double track = System.Math.Atan2(Vector3d.Dot(horiz, vessel.east), Vector3d.Dot(horiz, vessel.north));
            return (float)((track * (180.0 / System.Math.PI) + 360.0) % 360.0);
        }

        // Horizontal surface velocity in m/s, x = east, y = north.
        public static Vector2 GetGroundVelocity(this Vessel vessel)
        {
            Vector3d vel = vessel.GetSrfVelocity();
            return new Vector2((float)Vector3d.Dot(vel, vessel.east), (float)Vector3d.Dot(vel, vessel.north));
        }

        public static void GetFlightPathOffsets(this Vessel vessel, out float up, out float right)
        {
            // TODO: validate in flight
            Vector3d vel = vessel.GetSrfVelocity();
            Transform t = vessel.ReferenceTransform;
            double forward = Vector3d.Dot(vel, t.up);
            if (vel.sqrMagnitude < 1.0 || forward < 0.1)
            {
                up = 0f;
                right = 0f;
                return;
            }

            const double toDeg = 180.0 / System.Math.PI;
            up = (float)(System.Math.Atan2(Vector3d.Dot(vel, -t.forward), forward) * toDeg);
            right = (float)(System.Math.Atan2(Vector3d.Dot(vel, t.right), forward) * toDeg);
        }

        public static float GetOatC(this Vessel vessel)
        {
            return (float)(vessel.atmosphericTemperature - 273.15);
        }

        public static float GetAltitudeASL(this Vessel vessel)
        {
            // TODO: validate in flight
            return (float)vessel.altitude;
        }

        public static float GetAltitudeAslFt(this Vessel vessel)
        {
            return GetAltitudeASL(vessel) * Units.FeetPerMeter;
        }

        public static float GetVerticalSpeedFpm(this Vessel vessel)
        {
            // TODO: validate in flight
            return (float)vessel.verticalSpeed * Units.FpmPerMps;
        }

        public static float GetIasKt(this Vessel vessel)
        {
            // TODO: validate in flight
            return (float)vessel.indicatedAirSpeed * Units.KnotsPerMps;
        }

        public static float GetTasKt(this Vessel vessel)
        {
            // TODO: validate in flight
            return (float)vessel.srfSpeed * Units.KnotsPerMps;
        }

        public static float GetGsKt(this Vessel vessel)
        {
            // TODO: validate in flight
            return (float)vessel.horizontalSrfSpeed * Units.KnotsPerMps;
        }

        public static float GetAoA(this Vessel vessel)
        {
            // TODO: validate in flight
            Vector3d vel = vessel.GetSrfVelocity();
            if (vel.sqrMagnitude < 1e-4)
            {
                return 0f;
            }

            Vector3d local = vessel.ReferenceTransform.InverseTransformDirection(vel);
            return (float)(System.Math.Atan2(-local.z, local.y) * (180.0 / System.Math.PI));
        }
    }
}
