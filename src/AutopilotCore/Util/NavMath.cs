using System;
using UnityEngine;

namespace AutopilotCore
{
    public static class NavMath
    {
        private const double ToRad = Math.PI / 180.0;
        private const double ToDeg = 180.0 / Math.PI;

        public static double Distance(double radius, double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * ToRad;
            double dLon = (lon2 - lon1) * ToRad;
            double a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                       (Math.Cos(lat1 * ToRad) * Math.Cos(lat2 * ToRad) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
            return 2.0 * radius * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0.0, 1.0 - a)));
        }

        public static double Bearing(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = lat1 * ToRad;
            double phi2 = lat2 * ToRad;
            double dLon = (lon2 - lon1) * ToRad;
            double y = Math.Sin(dLon) * Math.Cos(phi2);
            double x = (Math.Cos(phi1) * Math.Sin(phi2)) - (Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLon));
            return ((Math.Atan2(y, x) * ToDeg) + 360.0) % 360.0;
        }

        // Signed distance from the great circle A->B; positive when P is right of the track.
        public static double CrossTrack(double radius, double latA, double lonA, double latB, double lonB, double latP, double lonP)
        {
            double angularAP = Distance(radius, latA, lonA, latP, lonP) / radius;
            double bearingAP = Bearing(latA, lonA, latP, lonP) * ToRad;
            double bearingAB = Bearing(latA, lonA, latB, lonB) * ToRad;
            return Math.Asin(Math.Sin(angularAP) * Math.Sin(bearingAP - bearingAB)) * radius;
        }

        // Point a fraction of the way along the great circle from 1 to 2.
        public static void Interpolate(double lat1, double lon1, double lat2, double lon2, double fraction, out double lat, out double lon)
        {
            double angle = Distance(1.0, lat1, lon1, lat2, lon2);
            if (angle < 1e-9)
            {
                lat = lat1;
                lon = lon1;
                return;
            }

            double phi1 = lat1 * ToRad;
            double phi2 = lat2 * ToRad;
            double lambda1 = lon1 * ToRad;
            double lambda2 = lon2 * ToRad;
            double a = Math.Sin((1.0 - fraction) * angle) / Math.Sin(angle);
            double b = Math.Sin(fraction * angle) / Math.Sin(angle);
            double x = (a * Math.Cos(phi1) * Math.Cos(lambda1)) + (b * Math.Cos(phi2) * Math.Cos(lambda2));
            double y = (a * Math.Cos(phi1) * Math.Sin(lambda1)) + (b * Math.Cos(phi2) * Math.Sin(lambda2));
            double z = (a * Math.Sin(phi1)) + (b * Math.Sin(phi2));
            lat = Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * ToDeg;
            lon = Math.Atan2(y, x) * ToDeg;
        }

        // Azimuthal equidistant east/north around a reference point: exact distance and bearing from
        // it, and no distortion blow-up near the poles.
        public static Vector2 LocalOffset(double radius, double latRef, double lonRef, double lat, double lon)
        {
            double distance = Distance(radius, latRef, lonRef, lat, lon);
            if (distance < 1e-6)
            {
                return Vector2.zero;
            }

            double bearing = Bearing(latRef, lonRef, lat, lon) * ToRad;
            return new Vector2((float)(distance * Math.Sin(bearing)), (float)(distance * Math.Cos(bearing)));
        }

        // Inverse of LocalOffset: the point reached from the reference along the offset's bearing and distance.
        public static void FromLocal(double radius, double latRef, double lonRef, double east, double north, out double lat, out double lon)
        {
            double delta = Math.Sqrt((east * east) + (north * north)) / radius;
            double theta = Math.Atan2(east, north);
            double phi1 = latRef * ToRad;
            double sinPhi2 = (Math.Sin(phi1) * Math.Cos(delta)) + (Math.Cos(phi1) * Math.Sin(delta) * Math.Cos(theta));
            double phi2 = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinPhi2)));
            double dLon = Math.Atan2(Math.Sin(theta) * Math.Sin(delta) * Math.Cos(phi1), Math.Cos(delta) - (Math.Sin(phi1) * sinPhi2));
            lat = phi2 * ToDeg;
            lon = (((lonRef + (dLon * ToDeg)) + 540.0) % 360.0) - 180.0;
        }

        // Angle (deg) to add to a direction measured at the reference point's map to get it in the
        // local north frame at the given point. Zero on a flat map; grows toward the poles.
        public static float GridConvergence(double latRef, double lonRef, double lat, double lon)
        {
            double forward = Bearing(latRef, lonRef, lat, lon);
            double back = Bearing(lat, lon, latRef, lonRef);
            return Mathf.DeltaAngle(0f, (float)(back - forward - 180.0));
        }
    }
}
