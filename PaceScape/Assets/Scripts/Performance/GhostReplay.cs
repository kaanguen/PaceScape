using System;
using System.Collections.Generic;

namespace PaceScape.Performance
{
    /// <summary>Replays measured PR sections, chaining the next longer record at the same absolute distance.</summary>
    public sealed class GhostReplay
    {
        private readonly List<PersonalBestRecord> _records = new List<PersonalBestRecord>();
        private PersonalBestRecord _record;
        private int _segment;
        private double _referenceTime;
        private double _fallbackSpeed;
        private bool _holding;

        /// <summary>Gets total ghost progress, independent of lane circumference and user measurements.</summary>
        public double DistanceMetres { get; private set; }
        /// <summary>Gets the current instantaneous reference speed, including measured standstill.</summary>
        public double SpeedMetresPerSecond { get; private set; }
        /// <summary>Gets the current reference distance, which may exceed the initially selected distance.</summary>
        public int ReferenceDistanceMetres { get; private set; }
        /// <summary>Gets whether the current reference is a measured speed trace.</summary>
        public bool HasTrace => !_holding && _record != null && _record.trace != null && _record.trace.Count >= 2;
        /// <summary>Gets whether the final speed is retained because no longer measured record exists.</summary>
        public bool IsHoldingSpeed => _holding;
        /// <summary>Gets the timestamp of the active measured record, or null for preset/example fallback.</summary>
        public string AchievedUtc => _record?.achievedUtc;
        /// <summary>Gets whether a genuine measured record supplies the active reference.</summary>
        public bool HasMeasuredReference => _record != null;

        /// <summary>Snapshots references so new user records cannot change an opponent midway through playback.</summary>
        public void Reset(PersonalBestBook book, int initialDistance, double fallbackSpeed, double startDistance = 0)
        {
            if (book == null || !book.IsValid() || initialDistance < 1 || initialDistance > 1000000 ||
                !IsFinite(fallbackSpeed) || fallbackSpeed <= 0 || !IsFinite(startDistance) || startDistance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialDistance));
            }
            _records.Clear();
            foreach (PersonalBestRecord source in book.records)
            {
                List<EffortPoint> trace = null;
                if (source.HasValidTrace())
                {
                    trace = new List<EffortPoint>(source.trace.Count);
                    foreach (EffortPoint point in source.trace)
                    {
                        trace.Add(new EffortPoint { seconds = point.seconds, distanceMetres = point.distanceMetres });
                    }
                }
                _records.Add(new PersonalBestRecord
                {
                    distanceMetres = source.distanceMetres,
                    seconds = source.seconds,
                    achievedUtc = source.achievedUtc,
                    trace = trace
                });
            }
            _records.Sort((left, right) => left.distanceMetres.CompareTo(right.distanceMetres));
            _fallbackSpeed = fallbackSpeed;
            DistanceMetres = startDistance;
            ReferenceDistanceMetres = initialDistance;
            _holding = false;
            _record = _records.Find(record => record.distanceMetres == initialDistance);
            while (DistanceMetres >= ReferenceDistanceMetres)
            {
                PersonalBestRecord next = FindNext();
                if (next == null)
                {
                    // Seeking after a mode change retains the last segment's speed, not its average.
                    SeekReference(Math.Min(DistanceMetres, ReferenceDistanceMetres));
                    _holding = true;
                    return;
                }
                _record = next;
                ReferenceDistanceMetres = next.distanceMetres;
            }
            SeekReference(DistanceMetres);
        }

        /// <summary>Integrates all crossed speed boundaries exactly and returns this step's physical travel.</summary>
        public double Advance(double seconds)
        {
            if (!IsFinite(seconds) || seconds <= 0)
            {
                return 0;
            }
            double before = DistanceMetres;
            while (seconds > 0)
            {
                if (_holding)
                {
                    DistanceMetres += seconds * SpeedMetresPerSecond;
                    break;
                }
                double available;
                if (HasTrace)
                {
                    EffortPoint end = _record.trace[_segment + 1];
                    available = Math.Max(0, end.seconds - _referenceTime);
                }
                else
                {
                    available = Math.Max(0, (ReferenceDistanceMetres - DistanceMetres) / SpeedMetresPerSecond);
                }
                double step = Math.Min(seconds, available);
                DistanceMetres += step * SpeedMetresPerSecond;
                _referenceTime += step;
                seconds -= step;
                if (step < available)
                {
                    break;
                }
                if (HasTrace && _segment + 2 < _record.trace.Count)
                {
                    DistanceMetres = _record.trace[_segment + 1].distanceMetres;
                    _segment++;
                    SetSegmentSpeed();
                    continue;
                }
                DistanceMetres = ReferenceDistanceMetres;
                PersonalBestRecord next = FindNext();
                if (next == null)
                {
                    _holding = true;
                }
                else
                {
                    _record = next;
                    ReferenceDistanceMetres = next.distanceMetres;
                    SeekReference(DistanceMetres);
                }
            }
            return DistanceMetres - before;
        }

        private PersonalBestRecord FindNext()
        {
            foreach (PersonalBestRecord record in _records)
            {
                if (record.distanceMetres > ReferenceDistanceMetres)
                {
                    return record;
                }
            }
            return null;
        }

        private void SeekReference(double distance)
        {
            _segment = 0;
            _referenceTime = 0;
            if (_record == null || _record.trace == null)
            {
                SpeedMetresPerSecond = _record != null ? _record.distanceMetres / _record.seconds : _fallbackSpeed;
                _referenceTime = distance / SpeedMetresPerSecond;
                return;
            }
            // Enter at the first arrival at this distance, retaining any stop beginning at the transition.
            while (_segment + 2 < _record.trace.Count)
            {
                double endDistance = _record.trace[_segment + 1].distanceMetres;
                if (endDistance < distance)
                {
                    _segment++;
                    continue;
                }
                if (endDistance == distance && endDistance > _record.trace[_segment].distanceMetres)
                {
                    _segment++;
                }
                break;
            }
            SetSegmentSpeed();
            EffortPoint start = _record.trace[_segment];
            _referenceTime = start.seconds + (SpeedMetresPerSecond > 0 ?
                Math.Max(0, distance - start.distanceMetres) / SpeedMetresPerSecond : 0);
        }

        private void SetSegmentSpeed()
        {
            EffortPoint start = _record.trace[_segment];
            EffortPoint end = _record.trace[_segment + 1];
            SpeedMetresPerSecond = (end.distanceMetres - start.distanceMetres) / (end.seconds - start.seconds);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
