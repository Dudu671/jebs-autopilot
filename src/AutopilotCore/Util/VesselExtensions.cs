using System;
using UnityEngine;

namespace AutopilotCore
{
    public static class VesselExtensions
    {
        public static float GetPitch(this Vessel vessel)
        {
            Vector3d forward = vessel.ReferenceTransform.up;
            Vector3d up = vessel.upAxis;
            return (float)(90.0 - Vector3d.Angle(forward, up));
        }

        public static float GetHeading(this Vessel vessel)
        {
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
            Vector3d up = vessel.upAxis;
            Vector3d right = vessel.ReferenceTransform.right;
            Vector3d dorsal = -vessel.ReferenceTransform.forward;
            double roll = System.Math.Atan2(Vector3d.Dot(right, up), Vector3d.Dot(dorsal, up));
            return (float)(-roll * (180.0 / System.Math.PI));
        }

        public static float GetTrack(this Vessel vessel)
        {
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

        // Velocity relative to the air mass. Stock KSP has no wind, so this matches surface velocity.
        public static Vector3d GetAirVelocity(this Vessel vessel)
        {
            return vessel.GetSrfVelocity() - Atmosphere.WindWorld(vessel);
        }

        // Velocity the lifting surfaces see: the air velocity where KWP applies the wind to their lift, else the
        // surface velocity, since without that the wind reaches only the drag.
        public static Vector3d GetAeroVelocity(this Vessel vessel)
        {
            return Atmosphere.LiftFeelsWind(vessel) ? vessel.GetAirVelocity() : (Vector3d)vessel.GetSrfVelocity();
        }

        // Horizontal wind in m/s, x = east, y = north. The vector points where the air is moving.
        public static Vector2 GetWindHorizontal(this Vessel vessel)
        {
            Vector3d wind = Atmosphere.WindWorld(vessel);
            return new Vector2((float)Vector3d.Dot(wind, vessel.east), (float)Vector3d.Dot(wind, vessel.north));
        }

        public static void GetFlightPathOffsets(this Vessel vessel, out float up, out float right)
        {
            // Flight path is over the ground. Angle of attack and sideslip use the air vector.
            Vector3d vel = vessel.GetSrfVelocity();
            Transform t = vessel.ReferenceTransform;
            double forward = Vector3d.Dot(vel, t.up);
            if (vel.sqrMagnitude < 1.0 || forward < 0.1)
            {
                up = 0f;
                right = 0f;
                return;
            }

            const double toDeg = 180.0 / Math.PI;
            up = (float)(Math.Atan2(Vector3d.Dot(vel, -t.forward), forward) * toDeg);
            right = (float)(Math.Atan2(Vector3d.Dot(vel, t.right), forward) * toDeg);
        }

        // Radians, positive with the air from the right of the nose (needs nose-right yaw).
        public static float GetSideslip(this Vessel vessel)
        {
            Vector3d air = vessel.GetAeroVelocity();
            Transform t = vessel.ReferenceTransform;
            double forward = Vector3d.Dot(air, t.up);
            if (air.sqrMagnitude < 1.0 || forward < 0.1)
            {
                return 0f;
            }

            return (float)Math.Atan2(Vector3d.Dot(air, t.right), forward);
        }

        public static float GetOatC(this Vessel vessel)
        {
            return Atmosphere.TemperatureK(vessel) - 273.15f;
        }

        public static float GetAltitudeASL(this Vessel vessel)
        {
            return (float)vessel.altitude;
        }

        public static float GetAltitudeAslFt(this Vessel vessel)
        {
            return GetAltitudeASL(vessel) * Units.FeetPerMeter;
        }

        public static float GetVerticalSpeedFpm(this Vessel vessel)
        {
            return (float)vessel.verticalSpeed * Units.FpmPerMps;
        }

        public static float GetIasKt(this Vessel vessel)
        {
            return (float)CalibratedAirspeed(vessel, vessel.GetAirVelocity().magnitude) * Units.KnotsPerMps;
        }

        // IAS of the velocity the wings see, which sets the stall margin; equal to IAS without wind.
        public static float GetAeroIasKt(this Vessel vessel)
        {
            return (float)CalibratedAirspeed(vessel, vessel.GetAeroVelocity().magnitude) * Units.KnotsPerMps;
        }

        public static float GetTasKt(this Vessel vessel)
        {
            return (float)vessel.GetAirVelocity().magnitude * Units.KnotsPerMps;
        }

        public static float GetMach(this Vessel vessel)
        {
            return (float)(vessel.GetAirVelocity().magnitude / vessel.GetSpeedOfSound());
        }

        public static double GetSpeedOfSound(this Vessel vessel)
        {
            double temperature = Math.Max(Atmosphere.TemperatureK(vessel), 1.0);
            return Math.Sqrt(1.4 * 287.05 * temperature);
        }

        public static float GetGsKt(this Vessel vessel)
        {
            return (float)vessel.horizontalSrfSpeed * Units.KnotsPerMps;
        }

        // Positive with the nose above the airflow; transform.forward points out of the belly.
        public static float GetAoA(this Vessel vessel)
        {
            Vector3d vel = vessel.GetAeroVelocity();
            if (vel.sqrMagnitude < 1e-4)
            {
                return 0f;
            }

            Vector3d local = vessel.ReferenceTransform.InverseTransformDirection(vel);
            return (float)(Math.Atan2(local.z, local.y) * (180.0 / Math.PI));
        }

        // rad/s: x = nose up, y = right wing down, z = nose right.
        // Uses the world-space rigidbody rate; vessel.angularVelocity is already vessel-local.
        // Unity is left-handed, hence the negations.
        public static bool TryGetBodyRates(this Vessel vessel, out Vector3 rates)
        {
            rates = Vector3.zero;
            Rigidbody rb = vessel.rootPart != null ? vessel.rootPart.Rigidbody : null;
            if (rb == null || vessel.ReferenceTransform == null)
            {
                return false;
            }

            Vector3 local = vessel.ReferenceTransform.InverseTransformDirection(rb.angularVelocity);
            if (float.IsNaN(local.x) || float.IsNaN(local.y) || float.IsNaN(local.z))
            {
                return false;
            }

            rates = new Vector3(-local.x, -local.y, -local.z);
            return true;
        }

        // Dynamic pressure relative to sea level at 110 kt.
        public static float GetDynamicRatio(this Vessel vessel)
        {
            const float reference = 0.5f * 1.223f * 56.6f * 56.6f;
            float speed = Mathf.Max((float)vessel.GetAirVelocity().magnitude, 1f);
            float density = Mathf.Max((float)vessel.atmDensity, 0f);
            return Mathf.Clamp(0.5f * density * speed * speed / reference, 0.08f, 5f);
        }

        public static float GetGravity(this Vessel vessel)
        {
            return (float)FlightGlobals.getGeeForceAtPosition(vessel.CoM).magnitude;
        }

        // Subsonic pitot conversion so indicated airspeed follows the air mass, not the ground.
        public static double CalibratedAirspeed(Vessel vessel, double tas)
        {
            double pressure = Atmosphere.PressurePa(vessel);
            double temperature = Atmosphere.TemperatureK(vessel);
            if (tas < 0.1 || pressure < 1.0 || temperature < 1.0)
            {
                return 0.0;
            }

            double mach = tas / Math.Sqrt(1.4 * 287.05 * temperature);
            double impact = pressure * (Math.Pow(1.0 + (0.2 * mach * mach), 3.5) - 1.0);
            const double seaLevelPressure = 101325.0;
            const double seaLevelSoundSpeed = 340.294;
            double term = Math.Pow((impact / seaLevelPressure) + 1.0, 2.0 / 7.0) - 1.0;
            if (term <= 0.0)
            {
                return 0.0;
            }

            return seaLevelSoundSpeed * Math.Sqrt(5.0 * term);
        }
    }
}
