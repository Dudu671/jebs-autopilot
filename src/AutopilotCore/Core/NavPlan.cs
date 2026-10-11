using System.Collections.Generic;

namespace AutopilotCore
{
    public sealed class NavPlan
    {
        private readonly List<Waypoint> _waypoints = new List<Waypoint>();

        public int Count
        {
            get { return _waypoints.Count; }
        }

        // Equal to Count once the last waypoint has been passed.
        public int ActiveIndex { get; private set; }

        // True when the active leg starts at the aircraft instead of the previous waypoint.
        public bool DirectTo { get; private set; }

        public bool HasActive
        {
            get { return ActiveIndex < _waypoints.Count; }
        }

        public Waypoint Active
        {
            get { return _waypoints[ActiveIndex]; }
        }

        public Waypoint this[int index]
        {
            get { return _waypoints[index]; }
        }

        public static string NameOf(int index)
        {
            return "Waypoint " + (index + 1);
        }

        public void Add(double latitude, double longitude)
        {
            _waypoints.Add(new Waypoint(latitude, longitude));
        }

        public void RemoveAt(int index)
        {
            _waypoints.RemoveAt(index);
            if (index < ActiveIndex)
            {
                ActiveIndex--;
            }
            else if (index == ActiveIndex)
            {
                DirectTo = true;
            }
        }

        public void Clear()
        {
            _waypoints.Clear();
            ActiveIndex = 0;
            DirectTo = false;
        }

        // Bumped on every direct-to so a repeated Go on the active waypoint restarts the leg.
        public int DirectToCount { get; private set; }

        public void SetActive(int index)
        {
            ActiveIndex = index;
            DirectTo = true;
            DirectToCount++;
        }

        public void Advance()
        {
            if (ActiveIndex < _waypoints.Count)
            {
                ActiveIndex++;
            }

            DirectTo = false;
        }
    }
}
