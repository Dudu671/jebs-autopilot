using System;
using System.Collections.Generic;
using UnityEngine;

namespace AutopilotCore
{
    // Where the vessel meets a level runway: its lowest point, wheels at full suspension extension,
    // and how far the nose can rise, pivoting on the lowest wheel, before anything aft of it touches.
    // The collider list is refreshed every second, since gear deploys and parts break off.
    public sealed class GroundGeometry
    {
        private const float RefreshSeconds = 1f;

        // Points this close behind the pivot belong to the gear itself.
        private const float MinAftDistance = 0.5f;

        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly Vector3[] _corners = new Vector3[8];
        private Guid _vesselId;
        private int _partCount = -1;
        private float _nextRefresh;

        // Altitude (m) of the lowest point above sea level.
        public double LowestAltitude { get; private set; }

        // Pitch attitude (deg) at which the tail touches with the main wheels on the runway;
        // infinity without deployed wheels.
        public float TailStrikePitch { get; private set; }

        public void Update(Vessel vessel)
        {
            if (vessel.id != _vesselId || vessel.parts.Count != _partCount || Time.time >= _nextRefresh)
            {
                Refresh(vessel);
            }

            Vector3 up = vessel.upAxis;
            Vector3 com = vessel.CoM;
            _points.Clear();
            double lowest = 0.0;
            Vector3 pivot = com;
            double pivotHeight = double.MaxValue;
            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider collider = _colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                WheelCollider wheel = collider as WheelCollider;
                if (wheel != null)
                {
                    Vector3 bottom = WheelBottom(wheel, up);
                    double height = Vector3.Dot(bottom - com, up);
                    lowest = Math.Min(lowest, height);
                    if (height < pivotHeight)
                    {
                        pivotHeight = height;
                        pivot = bottom;
                    }

                    continue;
                }

                int start = _points.Count;
                AddPoints(collider, up);
                for (int p = start; p < _points.Count; p++)
                {
                    lowest = Math.Min(lowest, Vector3.Dot(_points[p] - com, up));
                }
            }

            LowestAltitude = vessel.mainBody.GetAltitude(vessel.CoMD) + lowest;
            TailStrikePitch = pivotHeight < double.MaxValue ? vessel.GetPitch() + PitchMargin(vessel, pivot, up) : float.PositiveInfinity;
        }

        // Smallest nose-up rotation about the pivot that brings a point aft of it down to the pivot's level.
        private float PitchMargin(Vessel vessel, Vector3 pivot, Vector3 up)
        {
            Vector3 forward = Vector3.ProjectOnPlane(vessel.ReferenceTransform.up, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                return float.PositiveInfinity;
            }

            forward.Normalize();
            float margin = float.PositiveInfinity;
            for (int i = 0; i < _points.Count; i++)
            {
                Vector3 d = _points[i] - pivot;
                float aft = -Vector3.Dot(d, forward);
                if (aft < MinAftDistance)
                {
                    continue;
                }

                margin = Mathf.Min(margin, Mathf.Atan2(Vector3.Dot(d, up), aft) * Mathf.Rad2Deg);
            }

            return margin;
        }

        private void Refresh(Vessel vessel)
        {
            _vesselId = vessel.id;
            _partCount = vessel.parts.Count;
            _nextRefresh = Time.time + RefreshSeconds;
            _colliders.Clear();
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                _colliders.AddRange(vessel.parts[i].GetComponentsInChildren<Collider>(true));
            }
        }

        private static Vector3 WheelBottom(WheelCollider wheel, Vector3 up)
        {
            Transform t = wheel.transform;
            float scale = t.lossyScale.y;
            Vector3 hub = t.TransformPoint(wheel.center) - (t.up * (wheel.suspensionDistance * scale));
            return hub - (up * (wheel.radius * scale));
        }

        // The collider's bounding box corners, or the lowest point of its world bounds when the shape is unknown.
        private void AddPoints(Collider collider, Vector3 up)
        {
            Bounds local;
            if (!TryLocalBounds(collider, out local))
            {
                Bounds world = collider.bounds;
                _points.Add(world.ClosestPoint(world.center - (up * (world.extents.magnitude * 2f))));
                return;
            }

            Transform transform = collider.transform;
            Vector3 c = local.center;
            Vector3 e = local.extents;
            _corners[0] = new Vector3(c.x - e.x, c.y - e.y, c.z - e.z);
            _corners[1] = new Vector3(c.x + e.x, c.y - e.y, c.z - e.z);
            _corners[2] = new Vector3(c.x - e.x, c.y + e.y, c.z - e.z);
            _corners[3] = new Vector3(c.x + e.x, c.y + e.y, c.z - e.z);
            _corners[4] = new Vector3(c.x - e.x, c.y - e.y, c.z + e.z);
            _corners[5] = new Vector3(c.x + e.x, c.y - e.y, c.z + e.z);
            _corners[6] = new Vector3(c.x - e.x, c.y + e.y, c.z + e.z);
            _corners[7] = new Vector3(c.x + e.x, c.y + e.y, c.z + e.z);
            for (int i = 0; i < _corners.Length; i++)
            {
                _points.Add(transform.TransformPoint(_corners[i]));
            }
        }

        private static bool TryLocalBounds(Collider collider, out Bounds bounds)
        {
            BoxCollider box = collider as BoxCollider;
            if (box != null)
            {
                bounds = new Bounds(box.center, box.size);
                return true;
            }

            SphereCollider sphere = collider as SphereCollider;
            if (sphere != null)
            {
                bounds = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                return true;
            }

            CapsuleCollider capsule = collider as CapsuleCollider;
            if (capsule != null)
            {
                Vector3 size = Vector3.one * (capsule.radius * 2f);
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                bounds = new Bounds(capsule.center, size);
                return true;
            }

            MeshCollider mesh = collider as MeshCollider;
            if (mesh != null && mesh.sharedMesh != null)
            {
                bounds = mesh.sharedMesh.bounds;
                return true;
            }

            bounds = new Bounds();
            return false;
        }
    }
}
