// Validates and stores the fastest measured effort for each distance.

using System;
using System.Collections.Generic;

namespace PaceScape.Performance
{
    // A measured boundary relative to the beginning of a complete best effort.
    [Serializable]
    public sealed class EffortPoint
    {
        public double seconds;
        public double distanceMetres;
    }

    // One measured device-local personal best; demonstration values are never stored here.
    [Serializable]
    public sealed class PersonalBestRecord
    {
        public int distanceMetres;
        public double seconds;
        public string achievedUtc;
        public List<EffortPoint> trace;

        // Checks complete boundaries, including stationary intervals, without inventing samples.
        public bool HasValidTrace()
        {
            if (trace == null || trace.Count < 2 || trace.Count > 32000)
            {
                return false;
            }
            double previousTime = -1;
            double previousDistance = 0;
            foreach (EffortPoint point in trace)
            {
                if (point == null || double.IsNaN(point.seconds) || double.IsInfinity(point.seconds) ||
                    double.IsNaN(point.distanceMetres) || double.IsInfinity(point.distanceMetres) ||
                    point.seconds <= previousTime || point.distanceMetres < previousDistance ||
                    point.seconds > seconds || point.distanceMetres > distanceMetres)
                {
                    return false;
                }
                previousTime = point.seconds;
                previousDistance = point.distanceMetres;
            }
            EffortPoint first = trace[0];
            EffortPoint last = trace[trace.Count - 1];
            return first.seconds == 0 && first.distanceMetres == 0 &&
                Math.Abs(last.seconds - seconds) < 0.000001 &&
                Math.Abs(last.distanceMetres - distanceMetres) < 0.000001;
        }
    }

    // A versioned collection of real treadmill best efforts keyed by exact distance.
    [Serializable]
    public sealed class PersonalBestBook
    {
        public int version = 1;
        public List<PersonalBestRecord> records = new List<PersonalBestRecord>();

        // Gets a measured best time, or zero when this distance has not been completed.
        public double Find(int distance)
        {
            foreach (PersonalBestRecord record in records)
            {
                if (record.distanceMetres == distance)
                {
                    return record.seconds;
                }
            }
            return 0;
        }

        // Accepts the first complete effort and subsequent faster efforts only.
        public bool TryImprove(int distance, double seconds, string achievedUtc)
        {
            return TryImproveWithTrace(distance, seconds, achievedUtc, null);
        }

        // Stores an optional exact trace with a faster result, preserving legacy time-only records.
        public bool TryImproveWithTrace(int distance, double seconds, string achievedUtc, List<EffortPoint> trace)
        {
            if (distance < 1 || distance > 1000000 || !ValidTime(seconds) || (achievedUtc != null && achievedUtc.Length > 64))
            {
                return false;
            }
            var candidate = new PersonalBestRecord
            {
                distanceMetres = distance,
                seconds = seconds,
                achievedUtc = achievedUtc,
                trace = trace
            };
            if (trace != null && !candidate.HasValidTrace())
            {
                return false;
            }
            int retainedPoints = 0;
            foreach (PersonalBestRecord existing in records)
            {
                if (existing.distanceMetres != distance)
                {
                    retainedPoints += existing.trace?.Count ?? 0;
                }
            }
            // Keep the record even when its optional replay exceeds the device storage budget.
            if (retainedPoints + (trace?.Count ?? 0) > 32000)
            {
                trace = null;
            }
            if (trace != null)
            {
                var owned = new List<EffortPoint>(trace.Count);
                foreach (EffortPoint point in trace)
                {
                    owned.Add(new EffortPoint { seconds = point.seconds, distanceMetres = point.distanceMetres });
                }
                trace = owned;
            }
            foreach (PersonalBestRecord record in records)
            {
                if (record.distanceMetres != distance)
                {
                    continue;
                }
                if (seconds >= record.seconds)
                {
                    return false;
                }
                record.seconds = seconds;
                record.achievedUtc = achievedUtc;
                record.trace = trace;
                return true;
            }
            if (records.Count >= 1024)
            {
                return false;
            }
            records.Add(new PersonalBestRecord { distanceMetres = distance, seconds = seconds, achievedUtc = achievedUtc, trace = trace });
            return true;
        }

        // Rejects corrupt, duplicate or incompatible records before loading a saved book.
        public bool IsValid()
        {
            if (version != 1 || records == null || records.Count > 1024)
            {
                return false;
            }
            var distances = new HashSet<int>();
            int pointCount = 0;
            foreach (PersonalBestRecord record in records)
            {
                if (record == null || record.distanceMetres < 1 || record.distanceMetres > 1000000 ||
                    !ValidTime(record.seconds) || (record.achievedUtc != null && record.achievedUtc.Length > 64) || !distances.Add(record.distanceMetres))
                {
                    return false;
                }
                if (record.trace != null && record.trace.Count > 0 && !record.HasValidTrace())
                {
                    return false;
                }
                pointCount += record.trace?.Count ?? 0;
                if (pointCount > 32000)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ValidTime(double seconds)
        {
            return !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0 && seconds <= 1000000000;
        }
    }
}
