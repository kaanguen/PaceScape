namespace PaceScape.RunningTrack
{
    /// <summary>Gates runner movement independently from marker detection and anchor visibility.</summary>
    public sealed class RunningSession
    {
        /// <summary>Explicit operator-controlled running states.</summary>
        public enum RunState
        {
            Ready,
            Running,
            Paused
        }

        /// <summary>Gets the current running state; new sessions wait for an explicit start.</summary>
        public RunState State { get; private set; } = RunState.Ready;

        /// <summary>Starts only a ready session with tracked content.</summary>
        public bool TryStart(bool canRun)
        {
            if (State != RunState.Ready || !canRun)
            {
                return false;
            }

            State = RunState.Running;
            return true;
        }

        /// <summary>Pauses a running session without changing runner positions.</summary>
        public bool TryPause()
        {
            if (State != RunState.Running)
            {
                return false;
            }

            State = RunState.Paused;
            return true;
        }

        /// <summary>Resumes only a paused session with recovered tracked content.</summary>
        public bool TryResume(bool canRun)
        {
            if (State != RunState.Paused || !canRun)
            {
                return false;
            }

            State = RunState.Running;
            return true;
        }

        /// <summary>Returns to the waiting state; the owner resets its runner transforms separately.</summary>
        public void Reset()
        {
            State = RunState.Ready;
        }

        /// <summary>Advances visible content only while an explicit running session is active.</summary>
        public void Advance(RunningTrackMotion motion, float deltaTime, float paceMinutesPerKilometre)
        {
            if (State == RunState.Running)
            {
                motion?.Advance(deltaTime, paceMinutesPerKilometre);
            }
        }
    }
}
