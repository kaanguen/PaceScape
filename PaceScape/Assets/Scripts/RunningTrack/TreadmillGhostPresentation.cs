using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    /// <summary>Maps a signed progress gap along a selected lane without changing ghost speed.</summary>
    public sealed class TreadmillGhostPresentation
    {
        /// <summary>Gets whether the operator selected treadmill-relative presentation.</summary>
        public bool Enabled { get; private set; }
        /// <summary>Gets the legacy lateral setting retained for protocol compatibility.</summary>
        public float SideOffsetMetres { get; private set; } = 1.5f;
        /// <summary>Gets the maximum visible longitudinal gap in either direction.</summary>
        public float VisibilityDistanceMetres { get; private set; } = 10f;

        /// <summary>Accepts explicit display settings; these limits are technical bounds, not training parameters.</summary>
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

        /// <summary>Locks the anchor-local floor point beneath the head at calibration.</summary>
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

        /// <summary>Returns true only for a finite, in-range gap. Positive gap places the ghost ahead.</summary>
        public bool Evaluate(int lane, Vector3 origin, double ghostGapMetres, out Pose pose)
        {
            pose = Pose.identity;
            if (double.IsNaN(ghostGapMetres) || double.IsInfinity(ghostGapMetres))
            {
                return false;
            }
            // Hidden poses are bounded too, so very long sessions cannot move a renderer arbitrarily far away.
            float gap = (float)Math.Max(-VisibilityDistanceMetres, Math.Min(VisibilityDistanceMetres, ghostGapMetres));
            double referenceMetres = RunningTrackGeometry.FindNearestLaneMetres(lane, origin);
            pose = RunningTrackGeometry.EvaluateLane(lane, referenceMetres + gap);
            return Math.Abs(ghostGapMetres) <= VisibilityDistanceMetres;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>Small presentation-only wire snapshot; measurement and pacing remain separate.</summary>
    [Serializable]
    public sealed class GhostPresentationState
    {
        public string mode;
        public float sideOffsetMetres;
        public float visibilityDistanceMetres;
        public bool calibrated;
        public bool visible;
        public string status;
    }
}
