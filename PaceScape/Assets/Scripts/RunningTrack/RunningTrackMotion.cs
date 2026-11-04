using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    /// <summary>Moves one grounded object at constant physical speed along its selected lane centerline.</summary>
    public sealed class RunningTrackMotion
    {
        private readonly Transform _target;
        private readonly int _lane;
        private readonly float _groundOffset;
        private double _distance;

        /// <summary>Places the object behind the shared start line, preserving its grounded height and scale.</summary>
        public RunningTrackMotion(Transform target, int lane, float distanceBehindStartLine)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (lane < 1 || lane > RunningTrackGeometry.LaneCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lane));
            }

            if (float.IsNaN(distanceBehindStartLine) || float.IsInfinity(distanceBehindStartLine) || distanceBehindStartLine < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(distanceBehindStartLine));
            }

            Vector3 position = target.localPosition;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
            {
                throw new ArgumentException("The runner requires a finite local position.", nameof(target));
            }

            _target = target;
            _lane = lane;
            _groundOffset = target.localPosition.y;
            _distance = -distanceBehindStartLine % RunningTrackGeometry.GetLaneLength(lane);
            ApplyPose();
        }

        /// <summary>Advances by elapsed visible time; invalid or nonpositive time and pace leave the object still.</summary>
        public void Advance(float deltaTime, float paceMinutesPerKilometre)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f ||
                float.IsNaN(paceMinutesPerKilometre) || float.IsInfinity(paceMinutesPerKilometre) ||
                paceMinutesPerKilometre <= 0f)
            {
                return;
            }

            if (_target == null)
            {
                throw new InvalidOperationException("The runner object was removed.");
            }

            double speed = 1000.0 / ((double)paceMinutesPerKilometre * 60.0);
            _distance = (_distance + speed * deltaTime) % RunningTrackGeometry.GetLaneLength(_lane);
            ApplyPose();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ApplyPose()
        {
            Pose pose = RunningTrackGeometry.EvaluateLane(_lane, _distance);
            _target.SetLocalPositionAndRotation(pose.position + Vector3.up * _groundOffset, pose.rotation);
        }
    }
}
