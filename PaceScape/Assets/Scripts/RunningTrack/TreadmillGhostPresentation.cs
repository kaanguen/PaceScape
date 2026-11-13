// Places the ghost along the track using its signed distance from the treadmill runner.

using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    // Maps a signed progress gap along a selected lane without changing ghost speed.
    public sealed class TreadmillGhostPresentation
    {
        // Gets whether the operator selected treadmill-relative presentation.
        public bool Enabled { get; private set; }
        // Gets the signed lateral distance used by Side by Side.
        public float SideOffsetMetres { get; private set; } = 1.5f;
        // Gets the maximum visible longitudinal gap in either direction.
        public float VisibilityDistanceMetres { get; private set; } = 10f;

        // Accepts explicit display settings; these limits are technical bounds, not training parameters.
        public bool TryConfigure(string mode, float sideOffset, float visibilityDistance)
        {
            if ((mode != "Track" && mode != "TreadmillRelative") || !Finite(sideOffset) ||
                Mathf.Abs(sideOffset) < 0.5f || Mathf.Abs(sideOffset) > 5f || !Finite(visibilityDistance) ||
                visibilityDistance < 1f || visibilityDistance > 100f)
            {
                return false;
            }
            Enabled = mode == "TreadmillRelative";
            SideOffsetMetres = sideOffset;
            VisibilityDistanceMetres = visibilityDistance;
            return true;
        }

        // Locks the anchor-local floor point beneath the head at calibration.
        public static bool TryCalibrate(Vector3 localHeadPosition, out Vector3 origin)
        {
            origin = Vector3.zero;
            if (!Finite(localHeadPosition.x) || !Finite(localHeadPosition.y) || !Finite(localHeadPosition.z))
            {
                return false;
            }
            origin = new Vector3(localHeadPosition.x, 0, localHeadPosition.z);
            return true;
        }

        // Keeps the current point and tangent of the lane at the calibrated treadmill position.
        public static Pose EvaluateTrackPose(int lane, Vector3 origin, double travelledMetres, out double laneMetres)
        {
            return EvaluateDirectedTrackPose(lane, origin, travelledMetres, false, out laneMetres);
        }

        internal static Pose EvaluateDirectedTrackPose(int lane, Vector3 origin, double travelledMetres,
            bool reverse, out double laneMetres)
        {
            double referenceMetres = RunningTrackGeometry.FindNearestLaneMetres(lane, origin);
            Pose reference = RunningTrackGeometry.EvaluateLane(lane, referenceMetres);
            laneMetres = referenceMetres + (reverse ? -travelledMetres : travelledMetres);
            Pose current = RunningTrackGeometry.EvaluateLane(lane, laneMetres);
            Quaternion rotation = reference.rotation * Quaternion.Inverse(current.rotation);
            return new Pose(origin - rotation * current.position, rotation);
        }

        // Returns a ghost pose in the moving track's frame. Positive gap places it ahead.
        public bool Evaluate(int lane, double laneMetres, Pose trackPose, double ghostGapMetres, out Pose pose)
        {
            return EvaluateLane(lane, lane, laneMetres, trackPose, ghostGapMetres, true, false, out pose);
        }

        // Uses the next outer lane, or the inner neighbour at the outside edge.
        internal bool EvaluateAdjacentLane(int lane, double laneMetres, Pose trackPose, double ghostGapMetres, out Pose pose)
        {
            return EvaluateLane(lane, NeighbourLane(lane), laneMetres, trackPose, ghostGapMetres, true, false, out pose);
        }

        internal bool EvaluateReplay(int lane, double laneMetres, Pose trackPose, double ghostGapMetres,
            bool useAdjacentLane, out Pose pose)
        {
            return EvaluateDirectedGhost(lane, laneMetres, trackPose, ghostGapMetres, useAdjacentLane, true, false, out pose);
        }

        internal bool EvaluateDirectedGhost(int lane, double laneMetres, Pose trackPose, double ghostGapMetres,
            bool useAdjacentLane, bool isReplay, bool reverse, out Pose pose)
        {
            return EvaluateLane(lane, useAdjacentLane ? NeighbourLane(lane) : lane, laneMetres, trackPose,
                ghostGapMetres, !isReplay, reverse, out pose);
        }

        private static int NeighbourLane(int lane) => lane < RunningTrackGeometry.LaneCount ? lane + 1 : lane - 1;

        private bool EvaluateLane(int lane, int ghostLane, double laneMetres, Pose trackPose, double ghostGapMetres,
            bool limitVisibility, bool reverse, out Pose pose)
        {
            pose = Pose.identity;
            if (double.IsNaN(ghostGapMetres) || double.IsInfinity(ghostGapMetres))
            {
                return false;
            }
            // Hidden poses are bounded too, so very long sessions cannot move a renderer arbitrarily far away.
            double gap = limitVisibility
                ? Math.Max(-VisibilityDistanceMetres, Math.Min(VisibilityDistanceMetres, ghostGapMetres))
                : ghostGapMetres % RunningTrackGeometry.GetLaneLength(lane);
            Pose lanePose = RunningTrackGeometry.EvaluateLane(lane, laneMetres + (reverse ? -gap : gap));
            if (ghostLane != lane)
            {
                double ghostMetres = RunningTrackGeometry.FindNearestLaneMetres(ghostLane, lanePose.position);
                lanePose = RunningTrackGeometry.EvaluateLane(ghostLane, ghostMetres);
            }
            pose = new Pose(trackPose.position + trackPose.rotation * lanePose.position,
                trackPose.rotation * FaceDirection(lanePose.rotation, reverse));
            return !limitVisibility || Math.Abs(ghostGapMetres) <= VisibilityDistanceMetres;
        }

        // Uses the lane tangent for heading and offsets from the calibrated treadmill position.
        public Pose EvaluateSideBySide(int lane, Vector3 origin)
        {
            return EvaluateDirectedCompanion(lane, origin, false);
        }

        internal Pose EvaluateDirectedCompanion(int lane, Vector3 origin, bool reverse)
        {
            double referenceMetres = RunningTrackGeometry.FindNearestLaneMetres(lane, origin);
            Quaternion heading = FaceDirection(RunningTrackGeometry.EvaluateLane(lane, referenceMetres).rotation, reverse);
            return new Pose(origin + heading * Vector3.right * SideOffsetMetres, heading);
        }

        private static Quaternion FaceDirection(Quaternion heading, bool reverse)
        {
            return reverse ? heading * Quaternion.AngleAxis(180f, Vector3.up) : heading;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    // Small presentation-only wire snapshot; measurement and pacing remain separate.
    [Serializable]
    public sealed class GhostPresentationState
    {
        public string mode;
        public float sideOffsetMetres;
        public float visibilityDistanceMetres;
        public bool calibrated;
        public bool reverseDirection;
        public bool visible;
        public string status;
    }
}
