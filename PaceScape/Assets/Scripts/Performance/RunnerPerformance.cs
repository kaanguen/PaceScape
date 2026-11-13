// Coordinates belt measurements, local record storage, and the ghost's pace reference.

using System;
using System.IO;
using System.Threading.Tasks;
using PaceScape.RemoteControl;
using PaceScape.RunningTrack;
using PaceScape.Tracking;
using PaceScape.Treadmill;
using UnityEngine;

namespace PaceScape.Performance
{
    // Owns belt-only measurements and local records independently of avatar displacement.
    [DisallowMultipleComponent]
    public sealed class RunnerPerformance : MonoBehaviour
    {
        private TagTracker _tracker;
        private ServerTestScene _preview;
        private TreadmillInput _input;
        private PersonalBestBook _book = new PersonalBestBook();
        private readonly DistancePerformance _laps = new DistancePerformance();
        private readonly RollingDistanceBest _efforts = new RollingDistanceBest();
        private readonly GhostReplay _replay = new GhostReplay();
        private bool _hasReplay;
        private double _ghostDistance;
        private bool _hasMeasurementGap;
        private double _comparisonGhostOrigin;
        private double _lastTime;
        private double _lastSpeed;
        private bool _wasMeasuring;
        private string _path;
        private string _pendingJson;
        private Task _save;
        private string _savingJson;
        private bool _primaryValid;
        private float _retryAfter;
        private float _nextSaveAt;
        private bool _useDemoRecords;
        private bool _isLoaded;
        private string _storageError;
        private string _measurementStatus = "Waiting";

        // Gets whether real belt speed is currently fresh, including a stopped belt.
        public bool HasFreshInput => TryGetBeltSpeedMetresPerSecond(out _);

        // Converts the fresh FTMS sample from kilometres per hour to metres per second.
        public bool TryGetBeltSpeedMetresPerSecond(out double speed)
        {
            speed = 0;
            if (_input == null || !_input.TryGetSpeedKmh(out double speedKmh))
            {
                return false;
            }

            speed = speedKmh / 3.6;
            return true;
        }

        // Gets the same signed comparison used by telemetry, once continuity is established.
        public bool TryGetGhostGap(out double ghostGapMetres)
        {
            ghostGapMetres = _ghostDistance - _comparisonGhostOrigin - _laps.TotalDistanceMetres;
            return _wasMeasuring && !_hasMeasurementGap;
        }

        // Gets the selected fixed lap and rolling best-effort distance.
        public int ComparisonDistanceMetres => (int)_laps.DistanceMetres;
        // Gets whether clearly marked demonstration records may supply fallback ghost pace.
        public bool UseDemoRecords => _useDemoRecords;

        // Binds scene sources once; no scene searches occur during measurement.
        public void Bind(TagTracker tracker, TreadmillInput input, ServerTestScene preview)
        {
            _tracker = tracker;
            _preview = preview;
            _input = input;
            if (_input != null)
            {
                _input.MeasurementInterrupted += Interrupt;
            }
            _efforts.Improved += RecordBest;
            _efforts.Reset(ComparisonDistanceMetres);
            Load();
            ResetSession();
        }

        private RunningSession.RunState State => _tracker != null ? _tracker.RunState :
            _preview != null ? _preview.RunState : RunningSession.RunState.Ready;

        // Applies comparison settings; distance changes require a reset run.
        public bool TryConfigure(float distanceMetres, bool useDemoRecords)
        {
            if (float.IsNaN(distanceMetres) || float.IsInfinity(distanceMetres) || distanceMetres < 1 ||
                distanceMetres > 1000000 || distanceMetres != Math.Truncate(distanceMetres) ||
                (distanceMetres != ComparisonDistanceMetres && State != RunningSession.RunState.Ready) ||
                State == RunningSession.RunState.Running)
            {
                return false;
            }
            bool changed = distanceMetres != ComparisonDistanceMetres;
            if (_useDemoRecords != useDemoRecords)
            {
                _hasReplay = false;
            }
            _useDemoRecords = useDemoRecords;
            if (changed)
            {
                _laps.TryReset(distanceMetres);
                _efforts.Reset(distanceMetres);
                ResetSession();
            }
            return true;
        }

        // Clears this session's statistics while retaining real all-time records and settings.
        public void ResetSession()
        {
            _laps.TryReset(ComparisonDistanceMetres);
            _efforts.Reset(ComparisonDistanceMetres);
            _wasMeasuring = false;
            _lastTime = Time.realtimeSinceStartupAsDouble;
            _measurementStatus = "Waiting";
            _hasReplay = false;
            _ghostDistance = 0;
            _hasMeasurementGap = false;
        }

        // Invalidates the reference on mode changes without rewinding physical ghost progress.
        public void RunnerModeChanged()
        {
            _hasReplay = false;
        }

        // Advances one shared replay step after the scene has verified tracking and run state.
        public double AdvanceGhost(float deltaTime, double currentDistance, float fallbackPace)
        {
            if (!_hasReplay)
            {
                float averageFallback = GetTargetPace(RunningSession.RunnerMode.AllTimePersonalBest, fallbackPace);
                _replay.Reset(_book, ComparisonDistanceMetres, 1000.0 / (averageFallback * 60.0), currentDistance);
                _hasReplay = true;
            }
            double travel = _replay.Advance(deltaTime);
            _ghostDistance = _replay.DistanceMetres;
            return travel;
        }

        // Gets the replay's current speed, including genuine stationary intervals.
        public double GhostSpeedMetresPerSecond => _hasReplay ? _replay.SpeedMetresPerSecond : 0;

        // Tracks fixed-mode physical travel for progress comparison, never for user measurements.
        public void ObserveGhostDistance(double distance)
        {
            _ghostDistance = distance;
        }

        // Resolves instantaneous replay pace, or the existing measured/example/preset fallback.
        public float GetTargetPace(RunningSession.RunnerMode mode, float fallback)
        {
            if (mode == RunningSession.RunnerMode.SideBySide)
            {
                return TryGetBeltSpeedMetresPerSecond(out double speed) && speed > 0
                    ? (float)(1000 / (speed * 60))
                    : 0;
            }
            if (mode == RunningSession.RunnerMode.AllTimePersonalBest && _hasReplay)
            {
                return _replay.SpeedMetresPerSecond > 0 ? (float)(1000 / (_replay.SpeedMetresPerSecond * 60)) : 0;
            }
            double seconds = mode == RunningSession.RunnerMode.AllTimePersonalBest ? AllTimeSeconds : 0;
            return seconds > 0 ? (float)(seconds * 1000 / ComparisonDistanceMetres / 60) : fallback;
        }

        private double AllTimeSeconds
        {
            get
            {
                double real = _book.Find(ComparisonDistanceMetres);
                return real > 0 ? real : _useDemoRecords ? DemoSeconds(ComparisonDistanceMetres) : 0;
            }
        }

        private static double DemoSeconds(int distance)
        {
            // Fictional examples only, kept outside the persisted measured record book.
            switch (distance)
            {
                case 400: return 120;
                case 5000: return 1500;
                case 10000: return 3300;
                default: return 0;
            }
        }

        // Collects belt measurements after the scene has updated ghost progress.
        private void LateUpdate()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double delta = now - _lastTime;
            _lastTime = now;
            double speed = 0;
            bool hasSpeed = _input != null && _input.TryGetSpeedKmh(out speed);
            bool canMeasure = State == RunningSession.RunState.Running && hasSpeed &&
                _tracker != null && !_tracker.IsManual && _tracker.CanRun;
            if (canMeasure && _wasMeasuring && delta > 0 && delta <= 2)
            {
                // Hold the previous known sample across its interval; never use avatar smoothing as data.
                _laps.Advance(delta, _lastSpeed);
                _efforts.Advance(delta, _lastSpeed);
                _measurementStatus = _efforts.CapacityExceeded ? "CapacityExceeded" :
                    _laps.IsLapValid ? "Measuring" : "InterruptedLap";
            }
            else
            {
                if (State == RunningSession.RunState.Running && _ghostDistance > 0 &&
                    (!canMeasure || delta > 2 || delta <= 0))
                {
                    _hasMeasurementGap = true;
                }
                Interrupt();
                _measurementStatus = State != RunningSession.RunState.Running ? "Paused" :
                    "NoSignal";
            }
            if (canMeasure && !_wasMeasuring && !_hasMeasurementGap)
            {
                _comparisonGhostOrigin = _ghostDistance;
            }
            _wasMeasuring = canMeasure;
            _lastSpeed = speed;
            PollSave();
            // Presentation follows measurement in the same frame and cannot feed back into either clock.
            _tracker?.ApplyGhostPresentation();
        }

        private void Interrupt()
        {
            if (_wasMeasuring)
            {
                _hasMeasurementGap = true;
            }
            _wasMeasuring = false;
            _laps.Interrupt();
            _efforts.Interrupt();
        }

        private void RecordBest(double distance, double seconds)
        {
            double previous = _book.Find((int)distance);
            if (previous > 0 && seconds >= previous)
            {
                return;
            }
            if (!_book.TryImproveWithTrace((int)distance, seconds, DateTime.UtcNow.ToString("O"),
                _efforts.CopyImprovedTrace()))
            {
                return;
            }
            _pendingJson = JsonUtility.ToJson(_book);
        }

        // Loads a validated primary record book or recovers from its backup.
        private void Load()
        {
            if (_isLoaded)
            {
                return;
            }
            _isLoaded = true;
            _path = Path.Combine(Application.persistentDataPath, "pacescape-treadmill-personal-bests.json");
            foreach (string candidate in new[] { _path, _path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }
                    PersonalBestBook loaded = Read(candidate);
                    _book = loaded;
                    _primaryValid = candidate == _path;
                    return;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
                {
                    _storageError = $"Could not load records ({exception.GetType().Name}); checking backup.";
                }
            }
        }

        private static PersonalBestBook Read(string path)
        {
            if (new FileInfo(path).Length > 8 * 1024 * 1024)
            {
                throw new InvalidDataException("Record file is too large.");
            }
            PersonalBestBook loaded = JsonUtility.FromJson<PersonalBestBook>(File.ReadAllText(path));
            if (loaded == null || !loaded.IsValid())
            {
                throw new InvalidDataException("Invalid personal best records.");
            }
            return loaded;
        }

        // Coalesces pending snapshots and retries failed writes after a delay.
        private void PollSave(bool force = false)
        {
            if (_save != null)
            {
                if (!_save.IsCompleted)
                {
                    return;
                }
                if (_save.IsFaulted)
                {
                    _storageError = $"Could not save records ({_save.Exception.GetBaseException().GetType().Name}); retry pending.";
                    if (_pendingJson == null)
                    {
                        _pendingJson = _savingJson;
                    }
                    _retryAfter = Time.unscaledTime + 3f;
                }
                else
                {
                    _primaryValid = true;
                    _storageError = null;
                }
                _save = null;
            }
            if (_pendingJson == null || Time.unscaledTime < _retryAfter || (!force && Time.unscaledTime < _nextSaveAt))
            {
                return;
            }
            string json = _pendingJson;
            string path = _path;
            bool preservePrimary = _primaryValid;
            _pendingJson = null;
            _nextSaveAt = Time.unscaledTime + 5f;
            _savingJson = json;
            _save = Task.Run(() => Save(path, json, preservePrimary));
        }

        // Writes a temporary snapshot before replacing the primary record file.
        private static void Save(string path, string json, bool preservePrimary)
        {
            string temporary = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary, json);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, preservePrimary ? path + ".bak" : null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        // Packages measurement, replay, and save status for the dashboard.
        internal PerformanceState CaptureState()
        {
            double real = _book.Find(ComparisonDistanceMetres);
            return new PerformanceState
            {
                ghostReplayVersion = 1,
                ghostReferenceDistanceMetres = _hasReplay ? _replay.ReferenceDistanceMetres : ComparisonDistanceMetres,
                ghostPlayback = !_hasReplay ? "NotStarted" : _replay.IsHoldingSpeed ? "HoldingSpeed" :
                    _replay.HasTrace ? "RecordedTrace" : _replay.HasMeasuredReference ? "RecordAverage" : "Fallback",
                ghostReferenceAchievedUtc = _hasReplay ? _replay.AchievedUtc : null,
                ghostDistanceMetres = _ghostDistance,
                hasProgressComparison = (_tracker == null || !_tracker.IsSideBySide) && _wasMeasuring && !_hasMeasurementGap,
                userLeadMetres = _laps.TotalDistanceMetres - (_ghostDistance - _comparisonGhostOrigin) +
                    (_tracker != null ? _tracker.RelativeStartOffset : 0),
                comparisonDistanceMetres = ComparisonDistanceMetres,
                useDemoRecords = _useDemoRecords,
                measuredDistanceMetres = _laps.TotalDistanceMetres,
                measuredSeconds = _laps.ElapsedSeconds,
                lapNumber = _laps.CompletedLaps + 1,
                lapDistanceMetres = _laps.LapDistanceMetres,
                lapSeconds = _laps.LapSeconds,
                lapValid = _laps.IsLapValid,
                lapAverageSpeedKmh = _laps.LapSeconds > 0 ? _laps.LapDistanceMetres / _laps.LapSeconds * 3.6 : 0,
                lastLapSeconds = _laps.LastLapSeconds,
                lastLapAverageSpeedKmh = _laps.LastLapSeconds > 0 ? ComparisonDistanceMetres / _laps.LastLapSeconds * 3.6 : 0,
                sessionBestSeconds = _efforts.BestSeconds,
                allTimeBestSeconds = real,
                targetExampleSeconds = real <= 0 && _useDemoRecords ? DemoSeconds(ComparisonDistanceMetres) : 0,
                measurementStatus = _measurementStatus,
                storageError = _storageError,
                savePending = _pendingJson != null || _save != null,
                recordCount = _book.records.Count,
                records = CopyVisibleRecords(),
                laps = CopyVisibleLaps()
            };
        }

        private PersonalBestRecord[] CopyVisibleRecords()
        {
            // Leave headroom for tracking and BLE state within the relay's existing message-size limit.
            int count = Math.Min(32, _book.records.Count);
            var visible = new PersonalBestRecord[count];
            _book.records.CopyTo(0, visible, 0, count);
            for (int i = count; i < _book.records.Count; i++)
            {
                if (_book.records[i].distanceMetres == ComparisonDistanceMetres)
                {
                    visible[count - 1] = _book.records[i];
                    break;
                }
            }
            // Detailed replay data is local-only: never exceed the WebSocket relay's 16 KiB limit.
            for (int i = 0; i < visible.Length; i++)
            {
                PersonalBestRecord record = visible[i];
                visible[i] = new PersonalBestRecord
                {
                    distanceMetres = record.distanceMetres,
                    seconds = record.seconds,
                    achievedUtc = record.achievedUtc
                };
            }
            return visible;
        }

        private LapResult[] CopyVisibleLaps()
        {
            LapResult[] history = _laps.CopyHistory();
            if (history.Length <= 20)
            {
                return history;
            }
            var visible = new LapResult[20];
            Array.Copy(history, history.Length - visible.Length, visible, 0, visible.Length);
            return visible;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Interrupt();
                PollSave(true);
            }
        }
        private void OnDisable()
        {
            Interrupt();
            PollSave(true);
        }
        private void OnDestroy()
        {
            if (_input != null)
            {
                _input.MeasurementInterrupted -= Interrupt;
            }
            // A queued snapshot is independent of destroyed Unity objects and follows any active writer.
            if (_pendingJson != null && _path != null)
            {
                string path = _path;
                string json = _pendingJson;
                Task prior = _save ?? Task.CompletedTask;
                bool preserve = _primaryValid;
                prior.ContinueWith(task => Save(path, json, preserve && !task.IsFaulted), TaskScheduler.Default);
            }
        }
    }

    [Serializable]
    internal sealed class PerformanceState
    {
        public int ghostReplayVersion;
        public int ghostReferenceDistanceMetres;
        public string ghostPlayback;
        public string ghostReferenceAchievedUtc;
        public double ghostDistanceMetres;
        public bool hasProgressComparison;
        public double userLeadMetres;
        public int comparisonDistanceMetres;
        public bool useDemoRecords;
        public double measuredDistanceMetres;
        public double measuredSeconds;
        public int lapNumber;
        public double lapDistanceMetres;
        public double lapSeconds;
        public bool lapValid;
        public double lapAverageSpeedKmh;
        public double lastLapSeconds;
        public double lastLapAverageSpeedKmh;
        public double sessionBestSeconds;
        public double allTimeBestSeconds;
        public double targetExampleSeconds;
        public string measurementStatus;
        public string storageError;
        public bool savePending;
        public int recordCount;
        public PersonalBestRecord[] records;
        public LapResult[] laps;
    }
}
