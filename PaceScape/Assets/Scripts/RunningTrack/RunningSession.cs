// Manages explicit run states and selects the ghost's pace reference.

using UnityEngine;

namespace PaceScape.RunningTrack
{
    // Gates runner movement independently from marker detection and anchor visibility.
    public sealed class RunningSession
    {
        // Explicit operator-controlled running states.
        public enum RunState
        {
            Ready,
            Running,
            Paused,
            Countdown
        }

        // Selects the source of the avatar's pace and its presentation.
        public enum RunnerMode
        {
            FixedPace = 1,
            AllTimePersonalBest = 2,
            SideBySide = 3,
            Manual = 4
        }

        // Gets the selected speed mode; resets retain this operator setting.
        public RunnerMode Mode { get; private set; } = RunnerMode.FixedPace;

        // Changes mode atomically, pausing an active run without resetting its position.
        public bool TrySetMode(string mode)
        {
            RunnerMode selected;
            switch (mode)
            {
                case nameof(RunnerMode.FixedPace):
                    selected = RunnerMode.FixedPace;
                    break;
                case nameof(RunnerMode.AllTimePersonalBest):
                    selected = RunnerMode.AllTimePersonalBest;
                    break;
                case nameof(RunnerMode.SideBySide):
                    selected = RunnerMode.SideBySide;
                    break;
                case nameof(RunnerMode.Manual):
                    selected = RunnerMode.Manual;
                    break;
                default:
                    return false;
            }

            if (Mode != selected)
            {
                TryPause();
                Mode = selected;
            }
            return true;
        }

        // Gets the current running state; new sessions wait for an explicit start.
        public RunState State { get; private set; } = RunState.Ready;

        /// <summary>Gets the remaining time before the requested start in seconds.</summary>
        public float CountdownRemaining { get; private set; }

        /// <summary>Reserves a ready run and begins the start countdown.</summary>
        public bool TryBeginCountdown(bool canRun)
        {
            if (State != RunState.Ready || !canRun)
            {
                return false;
            }

            CountdownRemaining = 3f;
            State = RunState.Countdown;
            return true;
        }

        /// <summary>Signals expiry once while keeping the start reserved until it is confirmed.</summary>
        public bool TickCountdown(float deltaSeconds)
        {
            if (State != RunState.Countdown || CountdownRemaining <= 0f || !(deltaSeconds > 0f))
            {
                return false;
            }

            CountdownRemaining = Mathf.Max(0f, CountdownRemaining - deltaSeconds);
            return CountdownRemaining == 0f;
        }

        /// <summary>Starts after expiry when the run is still eligible.</summary>
        public bool TryCompleteCountdown(bool canRun)
        {
            if (State != RunState.Countdown || CountdownRemaining > 0f || !canRun)
            {
                return false;
            }

            State = RunState.Running;
            return true;
        }

        // Starts only a ready session with tracked content.
        public bool TryStart(bool canRun)
        {
            if (State != RunState.Ready || !canRun)
            {
                return false;
            }

            State = RunState.Running;
            return true;
        }

        // Pauses a running session without changing runner positions.
        public bool TryPause()
        {
            if (State == RunState.Countdown)
            {
                Reset();
                return true;
            }

            if (State != RunState.Running)
            {
                return false;
            }

            State = RunState.Paused;
            return true;
        }

        // Resumes only a paused session with recovered tracked content.
        public bool TryResume(bool canRun)
        {
            if (State != RunState.Paused || !canRun)
            {
                return false;
            }

            State = RunState.Running;
            return true;
        }

        // Returns to the waiting state; the owner resets its runner transforms separately.
        public void Reset()
        {
            CountdownRemaining = 0f;
            State = RunState.Ready;
        }

        // Advances visible content only while an explicit running session is active.
        public void Advance(RunningTrackMotion motion, float deltaTime, float paceMinutesPerKilometre)
        {
            if (State == RunState.Running)
            {
                motion?.Advance(deltaTime, paceMinutesPerKilometre);
            }
        }
    }
}
