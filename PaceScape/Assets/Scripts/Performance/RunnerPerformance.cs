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
    /// <summary>Owns belt-only measurements and local records independently of avatar displacement.</summary>
    [DisallowMultipleComponent]
    public sealed class RunnerPerformance : MonoBehaviour
    {
        private TagTracker _tracker;
        private ServerTestScene _preview;
        private TreadmillInput _input;
        private PersonalBestBook _book = new PersonalBestBook();
        private readonly DistancePerformance _laps = new DistancePerformance();
        private readonly RollingDistanceBest _efforts = new RollingDistanceBest();
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

        /// <summary>Gets the selected fixed lap and rolling best-effort distance.</summary>
        public int ComparisonDistanceMetres => (int)_laps.DistanceMetres;
        /// <summary>Gets whether clearly marked demonstration records may supply fallback ghost pace.</summary>
        public bool UseDemoRecords => _useDemoRecords;

        /// <summary>Binds scene sources once; no scene searches occur during measurement.</summary>
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

        /// <summary>Applies comparison settings; distance changes require a reset run.</summary>
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
            _useDemoRecords = useDemoRecords;
            if (changed)
            {
                _laps.TryReset(distanceMetres);
                _efforts.Reset(distanceMetres);
                ResetSession();
            }
            return true;
        }

        /// <summary>Clears this session's statistics while retaining real all-time records and settings.</summary>
        public void ResetSession()
        {
            _laps.TryReset(ComparisonDistanceMetres);
            _efforts.Reset(ComparisonDistanceMetres);
            _wasMeasuring = false;
            _lastTime = Time.realtimeSinceStartupAsDouble;
            _measurementStatus = "Waiting";
        }

        /// <summary>Resolves constant average pace; a real slower first result still replaces target/demo fallback.</summary>
        public float GetTargetPace(RunningSession.RunnerMode mode, float fallback)
        {
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

        private void LateUpdate()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double delta = now - _lastTime;
            _lastTime = now;
            double speed = 0;
            bool hasSpeed = _input != null && _input.TryGetSpeedKmh(out speed);
            bool canMeasure = State == RunningSession.RunState.Running && hasSpeed &&
                _tracker != null && _tracker.CanRun;
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
                Interrupt();
                _measurementStatus = State != RunningSession.RunState.Running ? "Paused" :
                    "NoSignal";
            }
            _wasMeasuring = canMeasure;
            _lastSpeed = speed;
            PollSave();
        }

        private void Interrupt()
        {
            _wasMeasuring = false;
            _laps.Interrupt();
            _efforts.Interrupt();
        }

        private void RecordBest(double distance, double seconds)
        {
            if (!_book.TryImprove((int)distance, seconds, DateTime.UtcNow.ToString("O")))
            {
                return;
            }
            _pendingJson = JsonUtility.ToJson(_book);
        }

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
            if (new FileInfo(path).Length > 1024 * 1024)
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

        internal PerformanceState CaptureState()
        {
            double real = _book.Find(ComparisonDistanceMetres);
            return new PerformanceState
            {
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
