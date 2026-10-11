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

        // Flat east/north approximation around a reference point; fine within a map view.
        public static Vector2 LocalOffset(double radius, double latRef, double lonRef, double lat, double lon)
        {
            double cosLat = Math.Max(0.05, Math.Cos(latRef * ToRad));
            double east = Mathf.DeltaAngle((float)lonRef, (float)lon) * ToRad * radius * cosLat;
            double north = (lat - latRef) * ToRad * radius;
            return new Vector2((float)east, (float)north);
        }

        public static void FromLocal(double radius, double latRef, double lonRef, double east, double north, out double lat, out double lon)
        {
            double cosLat = Math.Max(0.05, Math.Cos(latRef * ToRad));
            lat = Math.Max(-89.9, Math.Min(89.9, latRef + (north / radius * ToDeg)));
            lon = lonRef + (east / (radius * cosLat) * ToDeg);
            lon = ((lon + 540.0) % 360.0) - 180.0;
        }
    }
}
