using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutopilotCore
{
    // Speeds at which the stock lift of every lifting surface carries the weight in level flight, wings
    // level. Angles of attack are taken in the vessel's own frame, so it works on the ground and in the
    // air: on a level runway the angle of attack is the pitch attitude. Body lift is left out, which
    // errs on the fast side.
    public sealed class LiftModel
    {
        private const float ReferenceSpeed = 50f;
        private const float ScanStep = 1f;
        private const int SpeedIterations = 3;

        // Below this speed the lift the game applies is too small to check the model against.
        private const float MinCheckSpeed = 15f;

        private readonly List<ModuleLiftingSurface> _surfaces = new List<ModuleLiftingSurface>();
        private Guid _vesselId;
        private int _partCount = -1;

        public bool Valid { get; private set; }

        // IAS (kt): at the angle of attack of most lift up to the envelope's limit, and at the requested one.
        public float StallKt { get; private set; }

        public float AttitudeKt { get; private set; }

        // Lift the game applies over the lift this model predicts, at the current velocity; NaN when too slow.
        public float Check { get; private set; }

        // Aoa: the angle of attack (deg) for AttitudeKt.
        public void Update(Vessel vessel, float aoa)
        {
            Valid = false;
            Check = float.NaN;
            Refresh(vessel);
            double density = vessel.atmDensity;
            Transform reference = vessel.ReferenceTransform;
            if (_surfaces.Count == 0 || density <= 0.0 || reference == null)
            {
                return;
            }

            // transform.forward points out of the belly.
            Vector3 forward = reference.up;
            Vector3 dorsal = -reference.forward;
            float weight = (float)vessel.totalMass * vessel.GetGravity();
            float soundSpeed = (float)vessel.GetSpeedOfSound();

            float bestAoa = 0f;
            float bestLift = float.NegativeInfinity;
            for (float alpha = 0f; alpha <= Envelope.MaxAoa; alpha += ScanStep)
            {
                float lift = Lift(forward, dorsal, alpha, ReferenceSpeed, density, soundSpeed);
                if (lift > bestLift)
                {
                    bestLift = lift;
                    bestAoa = alpha;
                }
            }

            float stall = Solve(forward, dorsal, bestAoa, weight, density, soundSpeed);
            float attitude = Solve(forward, dorsal, aoa, weight, density, soundSpeed);
            Valid = !float.IsInfinity(stall) && !float.IsInfinity(attitude);
            StallKt = Ias(vessel, stall);
            AttitudeKt = Ias(vessel, attitude);
            Check = Compare(vessel, density, soundSpeed);
        }

        private static float Ias(Vessel vessel, float tas)
        {
            return (float)VesselExtensions.CalibratedAirspeed(vessel, tas) * Units.KnotsPerMps;
        }

        // Speed at which the lift at this angle of attack equals the weight; lift goes with speed squared,
        // and the iterations take in the Mach curve.
        private float Solve(Vector3 forward, Vector3 dorsal, float aoa, float weight, double density, float soundSpeed)
        {
            float speed = ReferenceSpeed;
            for (int i = 0; i < SpeedIterations; i++)
            {
                float lift = Lift(forward, dorsal, aoa, speed, density, soundSpeed);
                if (lift <= 0f)
                {
                    return float.PositiveInfinity;
                }

                speed *= Mathf.Sqrt(weight / lift);
            }

            return speed;
        }

        // Lift (kN) normal to the flight path at this angle of attack and speed.
        private float Lift(Vector3 forward, Vector3 dorsal, float aoa, float speed, double density, float soundSpeed)
        {
            float rad = aoa * Mathf.Deg2Rad;
            Vector3 velocity = (forward * Mathf.Cos(rad)) - (dorsal * Mathf.Sin(rad));
            Vector3 normal = (dorsal * Mathf.Cos(rad)) + (forward * Mathf.Sin(rad));
            return Sum(velocity * speed, normal, DynamicPressure(density, speed), speed / soundSpeed, false);
        }

        private float Compare(Vessel vessel, double density, float soundSpeed)
        {
            Vector3 velocity = vessel.GetAeroVelocity();
            float speed = velocity.magnitude;
            if (speed < MinCheckSpeed)
            {
                return float.NaN;
            }

            float predicted = Sum(velocity, Vector3.zero, DynamicPressure(density, speed), speed / soundSpeed, true);
            float applied = 0f;
            for (int i = 0; i < _surfaces.Count; i++)
            {
                applied += _surfaces[i].liftForce.magnitude;
            }

            return predicted > 1e-3f ? applied / predicted : float.NaN;
        }

        // Total lift along the axis, or the sum of the lift magnitudes.
        private float Sum(Vector3 velocity, Vector3 axis, double q, float mach, bool magnitude)
        {
            float total = 0f;
            for (int i = 0; i < _surfaces.Count; i++)
            {
                ModuleLiftingSurface surface = _surfaces[i];
                if (surface == null || surface.part == null)
                {
                    continue;
                }

                Vector3 nVel;
                Vector3 liftVector;
                float liftDot;
                float absDot;
                surface.SetupCoefficients(velocity, out nVel, out liftVector, out liftDot, out absDot);
                Vector3 lift = surface.GetLiftVector(liftVector, liftDot, absDot, q, mach);
                total += magnitude ? lift.magnitude : Vector3.Dot(lift, axis);
            }

            return total;
        }

        // Pa: the game passes dynamicPressurekPa * 1000 to GetLiftVector, and the lift multiplier brings it to kN.
        private static double DynamicPressure(double density, float speed)
        {
            return 0.5 * density * speed * speed;
        }

        private void Refresh(Vessel vessel)
        {
            if (vessel.id == _vesselId && vessel.parts.Count == _partCount)
            {
                return;
            }

            _vesselId = vessel.id;
            _partCount = vessel.parts.Count;
            _surfaces.Clear();
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                List<ModuleLiftingSurface> modules = vessel.parts[i].FindModulesImplementing<ModuleLiftingSurface>();
                for (int m = 0; m < modules.Count; m++)
                {
                    if (modules[m].deflectionLiftCoeff > 0f)
                    {
                        _surfaces.Add(modules[m]);
                    }
                }
            }
        }
    }
}
