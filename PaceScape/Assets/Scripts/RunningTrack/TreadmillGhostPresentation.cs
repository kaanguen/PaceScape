using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    /// <summary>Maps a signed progress gap into a calibrated floor frame without changing ghost speed.</summary>
    public sealed class TreadmillGhostPresentation
    {
        /// <summary>Gets whether the operator selected treadmill-relative presentation.</summary>
        public bool Enabled { get; private set; }
        /// <summary>Gets signed lateral separation: positive is right, negative is left.</summary>
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

        /// <summary>Locks a floor origin and heading from a deliberate calibration, ignoring head height and pitch.</summary>
        public static bool TryCalibrate(Vector3 localHeadPosition, Vector3 localHeadForward, out Pose origin)
        {
            origin = Pose.identity;
            if (!Finite(localHeadPosition.x) || !Finite(localHeadPosition.y) || !Finite(localHeadPosition.z) ||
                !Finite(localHeadForward.x) || !Finite(localHeadForward.y) || !Finite(localHeadForward.z))
            {
                return false;
            }
            Vector3 forward = Vector3.ProjectOnPlane(localHeadForward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
            {
                return false;
            }
            origin = new Pose(new Vector3(localHeadPosition.x, 0, localHeadPosition.z),
                Quaternion.LookRotation(forward.normalized, Vector3.up));
            return true;
        }

        /// <summary>Returns true only for a finite, in-range gap. Positive gap places the ghost ahead.</summary>
        public bool Evaluate(Pose origin, double ghostGapMetres, out Pose pose)
        {
            pose = origin;
            if (double.IsNaN(ghostGapMetres) || double.IsInfinity(ghostGapMetres))
            {
                return false;
            }
            // Hidden poses are bounded too, so very long sessions cannot move a renderer arbitrarily far away.
            float gap = (float)Math.Max(-VisibilityDistanceMetres, Math.Min(VisibilityDistanceMetres, ghostGapMetres));
            pose = new Pose(origin.position + origin.rotation * new Vector3(SideOffsetMetres, 0, gap), origin.rotation);
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
