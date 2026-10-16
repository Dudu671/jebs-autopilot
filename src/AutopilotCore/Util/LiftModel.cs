using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutopilotCore
{
    // Speeds at which the stock lift of every lifting surface carries the weight, for the vessel wings
    // level on the ground. A nose-up rotation is evaluated as the relative wind tilted below the nose,
    // leaving the airframe where it is. Body lift is left out, which errs on the fast side.
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

        // True airspeeds (m/s): at the attitude of most lift up to the envelope's AoA limit, and at the requested attitude.
        public float StallTas { get; private set; }

        public float LiftoffTas { get; private set; }

        // Lift the game applies over the lift this model predicts, at the current velocity; NaN when too slow.
        public float Check { get; private set; }

        // Pitch: the attitude (deg) for the liftoff speed.
        public void Update(Vessel vessel, float pitch)
        {
            Valid = false;
            Check = float.NaN;
            Refresh(vessel);
            double density = vessel.atmDensity;
            if (_surfaces.Count == 0 || density <= 0.0 || vessel.ReferenceTransform == null)
            {
                return;
            }

            Vector3 up = vessel.upAxis;
            Vector3 forward = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                return;
            }

            forward.Normalize();
            float weight = (float)vessel.totalMass * vessel.GetGravity();
            float soundSpeed = (float)vessel.GetSpeedOfSound();
            float current = vessel.GetPitch();

            float bestRotation = 0f;
            float bestLift = float.NegativeInfinity;
            for (float rotation = 0f; current + rotation <= Envelope.MaxAoa; rotation += ScanStep)
            {
                float lift = Lift(forward, up, rotation, ReferenceSpeed, density, soundSpeed);
                if (lift > bestLift)
                {
                    bestLift = lift;
                    bestRotation = rotation;
                }
            }

            StallTas = Solve(forward, up, bestRotation, weight, density, soundSpeed);
            LiftoffTas = Solve(forward, up, Mathf.Max(0f, pitch - current), weight, density, soundSpeed);
            Valid = !float.IsInfinity(StallTas) && !float.IsInfinity(LiftoffTas);
            Check = Compare(vessel, density, soundSpeed);
        }

        // Speed at which the lift at this rotation equals the weight; lift goes with speed squared, and
        // the iterations take in the Mach curve.
        private float Solve(Vector3 forward, Vector3 up, float rotation, float weight, double density, float soundSpeed)
        {
            float speed = ReferenceSpeed;
            for (int i = 0; i < SpeedIterations; i++)
            {
                float lift = Lift(forward, up, rotation, speed, density, soundSpeed);
                if (lift <= 0f)
                {
                    return float.PositiveInfinity;
                }

                speed *= Mathf.Sqrt(weight / lift);
            }

            return speed;
        }

        // Vertical lift (kN) with the nose this many degrees above the current attitude, moving level at this speed.
        private float Lift(Vector3 forward, Vector3 up, float rotation, float speed, double density, float soundSpeed)
        {
            float rad = rotation * Mathf.Deg2Rad;
            Vector3 wind = (forward * Mathf.Cos(rad)) - (up * Mathf.Sin(rad));
            Vector3 liftUp = (up * Mathf.Cos(rad)) + (forward * Mathf.Sin(rad));
            return Sum(wind * speed, liftUp, DynamicPressure(density, speed), speed / soundSpeed, false);
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
