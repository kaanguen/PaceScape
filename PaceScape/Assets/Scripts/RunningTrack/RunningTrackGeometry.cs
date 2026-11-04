using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    /// <summary>MarkerML2 oval dimensions and sampling, aligned to a marker on lane one's measurement line.</summary>
    public static class RunningTrackGeometry
    {
        public const double StraightLength = 84.39;
        public const double InnerRadius = 36.50;
        public const float LaneWidth = 1.22f;
        public const int LaneCount = 8;
        public const double MeasurementOffset = 0.30;
        public const double Radius = InnerRadius + MeasurementOffset;
        public const double LapLength = 2 * StraightLength + 2 * Math.PI * Radius;

        // Match MarkerML2's oval entry, ten nominal metres before the right bend.
        private const double EntryMetres = 390.0 * LapLength / 400.0;

        /// <summary>Samples the oval in metres; local zero is the marker and local +Z is the initial run direction.</summary>
        public static Vector3 Evaluate(double metres, float outwardOffset = 0f)
        {
            return EvaluatePath(metres, Radius, outwardOffset).position;
        }

        /// <summary>Gets the circumference of a lane centerline in metres.</summary>
        public static double GetLaneLength(int lane)
        {
            return 2 * StraightLength + 2 * Math.PI * GetLaneRadius(lane);
        }

        /// <summary>Samples actual metres along a selected lane centerline, starting at the shared start line.</summary>
        public static Pose EvaluateLane(int lane, double metres)
        {
            return EvaluatePath(metres, GetLaneRadius(lane), 0f);
        }

        private static double GetLaneRadius(int lane)
        {
            if (lane < 1 || lane > LaneCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lane));
            }

            return InnerRadius + (lane - 0.5) * LaneWidth;
        }

        private static Pose EvaluatePath(double metres, double pathRadius, float outwardOffset)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres) ||
                float.IsNaN(outwardOffset) || float.IsInfinity(outwardOffset))
            {
                throw new ArgumentOutOfRangeException(nameof(metres), "Track coordinates must be finite.");
            }

            double length = 2 * StraightLength + 2 * Math.PI * pathRadius;
            double entry = length - (LapLength - EntryMetres);
            double distance = ((entry + metres) % length + length) % length;
            double halfStraight = StraightLength / 2;
            double bend = Math.PI * pathRadius;
            double x;
            double z;
            double directionX;
            double directionZ;
            if (distance < bend)
            {
                double angle = -Math.PI / 2 + distance / pathRadius;
                x = halfStraight + pathRadius * Math.Cos(angle);
                z = pathRadius * Math.Sin(angle);
                directionX = -Math.Sin(angle);
                directionZ = Math.Cos(angle);
            }
            else if (distance < bend + StraightLength)
            {
                x = halfStraight - (distance - bend);
                z = pathRadius;
                directionX = -1;
                directionZ = 0;
            }
            else if (distance < 2 * bend + StraightLength)
            {
                double angle = Math.PI / 2 + (distance - bend - StraightLength) / pathRadius;
                x = -halfStraight + pathRadius * Math.Cos(angle);
                z = pathRadius * Math.Sin(angle);
                directionX = -Math.Sin(angle);
                directionZ = Math.Cos(angle);
            }
            else
            {
                x = -halfStraight + distance - 2 * bend - StraightLength;
                z = -pathRadius;
                directionX = 1;
                directionZ = 0;
            }

            double entryX = -halfStraight + entry - 2 * bend - StraightLength;
            // Rotate the original +X tangent into marker-local +Z and keep the entry at zero.
            Vector3 position = new Vector3((float)(-z - Radius + directionX * outwardOffset), 0f,
                (float)(x - entryX + directionZ * outwardOffset));
            Vector3 forward = new Vector3((float)-directionZ, 0f, (float)directionX);
            return new Pose(position, Quaternion.LookRotation(forward, Vector3.up));
        }
    }
}
