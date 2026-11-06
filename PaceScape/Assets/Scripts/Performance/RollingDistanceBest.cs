using System;
using System.Collections.Generic;

namespace PaceScape.Performance
{
    /// <summary>Finds the exact fastest window of a selected distance on a piecewise constant speed trace.</summary>
    public sealed class RollingDistanceBest
    {
        private struct Point
        {
            public double Distance;
            public double Time;
            public Point(double distance, double time)
            {
                Distance = distance;
                Time = time;
            }
        }
        private readonly List<Point> _points = new List<Point>();
        private int _nextCandidate;
        private double _distance;
        private double _time;
        private double _previousSpeed = -1;
        private double _length = 400;
        private double _bestStartTime;
        private double _bestEndTime;
        private double _bestStartDistance;
        private bool _isReportingBest;

        /// <summary>Copies the winning window during Improved, before old interpolation points are pruned.</summary>
        public List<EffortPoint> CopyImprovedTrace()
        {
            if (!_isReportingBest)
            {
                throw new InvalidOperationException("Copy the trace synchronously from Improved.");
            }
            if (_points.Count > 31998)
            {
                return null;
            }
            var trace = new List<EffortPoint>();
            trace.Add(new EffortPoint { seconds = 0, distanceMetres = 0 });
            foreach (Point point in _points)
            {
                if (point.Time > _bestStartTime && point.Time < _bestEndTime)
                {
                    trace.Add(new EffortPoint
                    {
                        seconds = point.Time - _bestStartTime,
                        distanceMetres = Math.Max(0, Math.Min(_length, point.Distance - _bestStartDistance))
                    });
                }
            }
            trace.Add(new EffortPoint { seconds = BestSeconds, distanceMetres = _length });
            return trace;
        }

        /// <summary>Gets the best complete continuous effort, or zero before a result.</summary>
        public double BestSeconds { get; private set; }
        /// <summary>Gets whether the bounded trace capacity has been exhausted.</summary>
        public bool CapacityExceeded { get; private set; }
        /// <summary>Raised when a complete continuous window improves the session record.</summary>
        public event Action<double, double> Improved;

        /// <summary>Begins a session for an already validated distance.</summary>
        public void Reset(double distance)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 1 || distance > 1000000)
            {
                throw new ArgumentOutOfRangeException(nameof(distance));
            }
            _length = distance;
            BestSeconds = 0;
            Interrupt();
        }

        /// <summary>Breaks the continuous trace across unknown intervals while retaining completed records.</summary>
        public void Interrupt()
        {
            _points.Clear();
            _nextCandidate = 0;
            _distance = 0;
            _time = 0;
            _previousSpeed = -1;
            CapacityExceeded = false;
        }

        /// <summary>Includes zero-speed time and evaluates both ends of each speed-change boundary.</summary>
        public void Advance(double seconds, double speedKmh)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 ||
                double.IsNaN(speedKmh) || double.IsInfinity(speedKmh) || speedKmh < 0)
            {
                Interrupt();
                return;
            }
            if (CapacityExceeded)
            {
                return;
            }
            double speed = speedKmh / 3.6;
            if (speed != _previousSpeed)
            {
                if (_points.Count >= 200000)
                {
                    CapacityExceeded = true;
                    return;
                }
                _points.Add(new Point(_distance, _time));
                _previousSpeed = speed;
            }
            double beforeDistance = _distance;
            double beforeTime = _time;
            _distance += speed * seconds;
            _time += seconds;
            if (speed <= 0 || _distance < _length)
            {
                return;
            }
            // A minimum occurs when either end crosses a speed boundary, not just at frame endpoints.
            while (_nextCandidate < _points.Count && _points[_nextCandidate].Distance + _length <= _distance)
            {
                double endDistance = _points[_nextCandidate].Distance + _length;
                if (endDistance >= beforeDistance)
                {
                    Evaluate(endDistance, beforeTime + (endDistance - beforeDistance) / speed);
                }
                _nextCandidate++;
            }
            Evaluate(_distance, _time);
            // Points before the rolling start are no longer needed. Keep its interpolation predecessor.
            int keep = FindStart(_distance - _length);
            if (keep > 1024)
            {
                _points.RemoveRange(0, keep);
                _nextCandidate -= keep;
            }
        }

        private int FindStart(double distance)
        {
            int low = 0;
            int high = _points.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (_points[middle].Distance <= distance)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return Math.Max(0, low - 1);
        }

        private void Evaluate(double endDistance, double endTime)
        {
            double startDistance = endDistance - _length;
            int index = FindStart(startDistance);
            Point before = _points[index];
            Point after = index + 1 < _points.Count ? _points[index + 1] : new Point(_distance, _time);
            double startTime = before.Time;
            if (after.Distance > before.Distance)
            {
                startTime += (startDistance - before.Distance) * (after.Time - before.Time) /
                    (after.Distance - before.Distance);
            }
            double duration = endTime - startTime;
            if (duration > 0 && (BestSeconds <= 0 || duration < BestSeconds - 0.0000001))
            {
                BestSeconds = duration;
                _bestStartTime = startTime;
                _bestEndTime = endTime;
                _bestStartDistance = startDistance;
                _isReportingBest = true;
                try
                {
                    Improved?.Invoke(_length, duration);
                }
                finally
                {
                    _isReportingBest = false;
                }
            }
        }
    }
}
