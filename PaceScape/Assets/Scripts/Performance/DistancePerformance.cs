// Accumulates belt distance and measures consecutive fixed-distance laps.

using System;
using System.Collections.Generic;

namespace PaceScape.Performance
{
    // Measures complete fixed-distance efforts from belt samples, independently of avatar motion.
    public sealed class DistancePerformance
    {
        private readonly List<LapResult> _history = new List<LapResult>();
        private double _lapStartedAt;
        private double _lapDistance;
        private bool _isLapValid = true;

        // Gets the selected measurement distance in metres.
        public double DistanceMetres { get; private set; } = 400;
        // Gets total measured distance in this session.
        public double TotalDistanceMetres { get; private set; }
        // Gets elapsed measured session time, including stopped belts.
        public double ElapsedSeconds { get; private set; }
        // Gets the number of completed distance blocks, including invalid blocks.
        public int CompletedLaps { get; private set; }
        // Gets progress within the current distance block.
        public double LapDistanceMetres => _lapDistance;
        // Gets elapsed time within the current distance block.
        public double LapSeconds => ElapsedSeconds - _lapStartedAt;
        // Gets whether the current block contains no measurement gaps.
        public bool IsLapValid => _isLapValid;
        // Gets the last valid completed effort time, or zero before a result.
        public double LastLapSeconds { get; private set; }
        // Gets the fastest valid completed effort this session, or zero before a result.
        public double BestSeconds { get; private set; }
        // Raised only for a fully measured distance block, including slower efforts.
        public event Action<double, double> EffortCompleted;

        // Resets session measurements while selecting a new distance.
        public bool TryReset(double distanceMetres)
        {
            // This is an engineering input bound, not a prescribed training distance.
            if (!IsFinite(distanceMetres) || distanceMetres < 1 || distanceMetres > 1000000)
            {
                return false;
            }
            DistanceMetres = distanceMetres;
            TotalDistanceMetres = 0;
            ElapsedSeconds = 0;
            _lapDistance = 0;
            _lapStartedAt = 0;
            CompletedLaps = 0;
            _history.Clear();
            LastLapSeconds = 0;
            BestSeconds = 0;
            _isLapValid = true;
            return true;
        }

        // Marks an unknown interval; a partially measured block cannot become a record.
        public void Interrupt()
        {
            if (_lapDistance > 0 || LapSeconds > 0)
            {
                _isLapValid = false;
            }
        }

        // Integrates one known constant-speed interval, interpolating exact distance boundaries.
        public void Advance(double seconds, double speedKmh)
        {
            if (!IsFinite(seconds) || seconds <= 0 || !IsFinite(speedKmh) || speedKmh < 0)
            {
                Interrupt();
                return;
            }
            double speed = speedKmh / 3.6;
            double remainingSeconds = seconds;
            while (remainingSeconds > 0)
            {
                double untilBoundary = speed > 0 ? (DistanceMetres - _lapDistance) / speed : double.PositiveInfinity;
                double step = Math.Min(remainingSeconds, untilBoundary);
                _lapDistance += speed * step;
                TotalDistanceMetres += speed * step;
                ElapsedSeconds += step;
                remainingSeconds -= step;
                if (untilBoundary > step)
                {
                    break;
                }
                double lapSeconds = LapSeconds;
                CompletedLaps++;
                if (_history.Count == 100)
                {
                    _history.RemoveAt(0);
                }
                _history.Add(new LapResult
                {
                    number = CompletedLaps,
                    seconds = lapSeconds,
                    averageSpeedKmh = _isLapValid && lapSeconds > 0 ? DistanceMetres / lapSeconds * 3.6 : 0,
                    isValid = _isLapValid
                });
                if (_isLapValid && lapSeconds > 0)
                {
                    LastLapSeconds = lapSeconds;
                    if (BestSeconds <= 0 || lapSeconds < BestSeconds)
                    {
                        BestSeconds = lapSeconds;
                    }
                    EffortCompleted?.Invoke(DistanceMetres, lapSeconds);
                }
                _lapDistance = 0;
                _lapStartedAt = ElapsedSeconds;
                _isLapValid = true;
            }
        }

        // Copies up to the latest 100 completed lap results for display.
        public LapResult[] CopyHistory()
        {
            return _history.ToArray();
        }

        private static bool IsFinite(double number)
        {
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }
    }

    // A completed distance block; invalid entries are display-only and never records.
    [Serializable]
    public sealed class LapResult
    {
        public int number;
        public double seconds;
        public double averageSpeedKmh;
        public bool isValid;
    }

}
