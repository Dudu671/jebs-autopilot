namespace AutopilotCore
{
    // A landing direction on a runway: the threshold landed over and the far end.
    public sealed class Runway
    {
        // Stock KSC runway ends; the surface height comes from a vessel parked on it.
        private const double KscWestLat = -0.0486697;
        private const double KscWestLon = -74.7220377;
        private const double KscEastLat = -0.0502131;
        private const double KscEastLon = -74.4884787;
        private const double KscElevation = 69.2;

        public static readonly Runway[] Ksc =
        {
            new Runway("09", KscWestLat, KscWestLon, KscEastLat, KscEastLon, KscElevation),
            new Runway("27", KscEastLat, KscEastLon, KscWestLat, KscWestLon, KscElevation),
        };

        public readonly string Name;
        public readonly double ThresholdLat;
        public readonly double ThresholdLon;
        public readonly double EndLat;
        public readonly double EndLon;
        public readonly double Elevation;

        private Runway(string name, double thresholdLat, double thresholdLon, double endLat, double endLon, double elevation)
        {
            Name = name;
            ThresholdLat = thresholdLat;
            ThresholdLon = thresholdLon;
            EndLat = endLat;
            EndLon = endLon;
            Elevation = elevation;
        }

        public float Course
        {
            get { return (float)NavMath.Bearing(ThresholdLat, ThresholdLon, EndLat, EndLon); }
        }

        public double Length(double radius)
        {
            return NavMath.Distance(radius, ThresholdLat, ThresholdLon, EndLat, EndLon);
        }
    }
}
