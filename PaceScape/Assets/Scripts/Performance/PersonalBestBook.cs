using System;
using System.Collections.Generic;

namespace PaceScape.Performance
{
    /// <summary>One measured device-local personal best; demonstration values are never stored here.</summary>
    [Serializable]
    public sealed class PersonalBestRecord
    {
        public int distanceMetres;
        public double seconds;
        public string achievedUtc;
    }

    /// <summary>A versioned collection of real treadmill best efforts keyed by exact distance.</summary>
    [Serializable]
    public sealed class PersonalBestBook
    {
        public int version = 1;
        public List<PersonalBestRecord> records = new List<PersonalBestRecord>();

        /// <summary>Gets a measured best time, or zero when this distance has not been completed.</summary>
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

        /// <summary>Accepts the first complete effort and subsequent faster efforts only.</summary>
        public bool TryImprove(int distance, double seconds, string achievedUtc)
        {
            if (distance < 1 || distance > 1000000 || !ValidTime(seconds) || (achievedUtc != null && achievedUtc.Length > 64))
            {
                return false;
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
                return true;
            }
            if (records.Count >= 1024)
            {
                return false;
            }
            records.Add(new PersonalBestRecord { distanceMetres = distance, seconds = seconds, achievedUtc = achievedUtc });
            return true;
        }

        /// <summary>Rejects corrupt, duplicate or incompatible records before loading a saved book.</summary>
        public bool IsValid()
        {
            if (version != 1 || records == null || records.Count > 1024)
            {
                return false;
            }
            var distances = new HashSet<int>();
            foreach (PersonalBestRecord record in records)
            {
                if (record == null || record.distanceMetres < 1 || record.distanceMetres > 1000000 ||
                    !ValidTime(record.seconds) || (record.achievedUtc != null && record.achievedUtc.Length > 64) || !distances.Add(record.distanceMetres))
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
