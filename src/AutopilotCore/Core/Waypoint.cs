namespace AutopilotCore
{
    public struct Waypoint
    {
        public readonly double Latitude;
        public readonly double Longitude;

        public Waypoint(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }
    }
}
