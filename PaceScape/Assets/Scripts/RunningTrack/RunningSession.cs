// Manages explicit run states and selects the ghost's pace reference.

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
            Paused
        }

        // Selects a preset pace, a personal best, or live belt speed.
        public enum RunnerMode
        {
            FixedPace = 1,
            AllTimePersonalBest = 2,
            SideBySide = 3
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
