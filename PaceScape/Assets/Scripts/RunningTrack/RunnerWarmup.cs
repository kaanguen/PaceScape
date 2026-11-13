using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    /// <summary>Plays an operator-defined sequence on a stationary humanoid runner.</summary>
    public sealed class RunnerWarmup
    {
        public const string JumpingJacks = "Mixamo_StartJumpingJacks";
        public const string AirSquat = "Mixamo_AirSquat";
        public const int MaxSteps = 20;
        private const float CountdownSeconds = 3f;
        private const float BlendSeconds = 0.2f;
        private static readonly int _idleState = StateHash("Idle");

        /// <summary>One exercise and its requested playback time in seconds.</summary>
        [Serializable]
        public struct Step
        {
            public string animationId;
            public float seconds;
        }

        /// <summary>Progress fields shared with the remote control client.</summary>
        [Serializable]
        public struct State
        {
            public bool active;
            public bool canStart;
            public string status;
            public int stepIndex;
            public int stepCount;
            public string animationId;
            public float stepElapsed;
            public float stepDuration;
            public float countdownRemaining;
        }

        private Animator _animator;
        private Step[] _steps;
        private int _stepIndex;
        private float _stepElapsed;
        private float _countdownRemaining;
        private string _status = "Idle";

        /// <summary>Whether the sequence currently owns the runner's animation.</summary>
        public bool Active => _steps != null;

        /// <summary>Gets the whole seconds to show before the first exercise starts.</summary>
        public int CountdownDisplay => Mathf.CeilToInt(_countdownRemaining);

        /// <summary>Whether the runner supports the complete warm-up catalogue.</summary>
        public static bool Supports(Animator animator)
        {
            return animator != null && animator.isActiveAndEnabled && animator.isInitialized && animator.isHuman &&
                animator.HasState(0, _idleState) && animator.HasState(0, StateHash(JumpingJacks)) &&
                animator.HasState(0, StateHash(AirSquat));
        }

        /// <summary>Checks the complete plan before allocating an avatar.</summary>
        public static bool IsValidPlan(Step[] steps)
        {
            if (steps == null || steps.Length < 1 || steps.Length > MaxSteps)
            {
                return false;
            }

            foreach (Step step in steps)
            {
                if ((step.animationId != JumpingJacks && step.animationId != AirSquat) ||
                    !(step.seconds >= 1f && step.seconds <= 300f))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Validates and snapshots the sequence before taking over the animation.</summary>
        public bool TryStart(Animator animator, Step[] steps)
        {
            if (Active || !IsValidPlan(steps) || !Supports(animator))
            {
                return false;
            }

            _steps = (Step[])steps.Clone();
            _stepIndex = 0;
            _stepElapsed = 0f;
            _countdownRemaining = CountdownSeconds;
            _status = "Countdown";
            _animator = animator;
            _animator.Play(_idleState, 0, 0f);
            return true;
        }

        /// <summary>Consumes visible elapsed time and advances through exercise boundaries.</summary>
        public void Tick(float deltaSeconds)
        {
            if (!Active || !(deltaSeconds > 0f) || float.IsInfinity(deltaSeconds))
            {
                return;
            }

            bool isStarting = _countdownRemaining > 0f;
            if (isStarting)
            {
                float countdownTime = Mathf.Min(deltaSeconds, _countdownRemaining);
                _countdownRemaining -= countdownTime;
                if (_countdownRemaining > 0f)
                {
                    return;
                }

                deltaSeconds -= countdownTime;
                _status = "Playing";
            }

            string previous = _steps[_stepIndex].animationId;
            _stepElapsed += deltaSeconds;
            while (_stepElapsed >= _steps[_stepIndex].seconds)
            {
                _stepElapsed -= _steps[_stepIndex].seconds;
                _stepIndex++;
                if (_stepIndex == _steps.Length)
                {
                    Stop("Completed");
                    return;
                }
            }
            if (isStarting || _steps[_stepIndex].animationId != previous)
            {
                PlayCurrent();
            }
        }

        /// <summary>Skips the active exercise or completes the sequence at its last step.</summary>
        public bool TryNext()
        {
            if (!Active || _countdownRemaining > 0f)
            {
                return false;
            }

            string previous = _steps[_stepIndex].animationId;
            _stepIndex++;
            _stepElapsed = 0f;
            if (_stepIndex == _steps.Length)
            {
                Stop("Completed");
            }
            else if (_steps[_stepIndex].animationId != previous)
            {
                PlayCurrent();
            }
            return true;
        }

        /// <summary>Returns the coach to idle and clears the active sequence.</summary>
        public bool Stop(string status = "Stopped")
        {
            if (!Active)
            {
                return false;
            }

            if (_animator != null && _animator.isInitialized)
            {
                _animator.CrossFadeInFixedTime(_idleState, BlendSeconds, 0, 0f);
            }
            _animator = null;
            _steps = null;
            _countdownRemaining = 0f;
            _status = status;
            return true;
        }

        /// <summary>Captures the current step and most recent outcome.</summary>
        public State CaptureState(bool canStart)
        {
            return new State
            {
                active = Active,
                canStart = canStart,
                status = _status,
                stepIndex = Active ? _stepIndex : -1,
                stepCount = Active ? _steps.Length : 0,
                animationId = Active ? _steps[_stepIndex].animationId : null,
                stepElapsed = Active ? _stepElapsed : 0f,
                stepDuration = Active ? _steps[_stepIndex].seconds : 0f,
                countdownRemaining = _countdownRemaining
            };
        }

        private void PlayCurrent()
        {
            _animator.CrossFadeInFixedTime(StateHash(_steps[_stepIndex].animationId), BlendSeconds, 0, _stepElapsed);
        }

        private static int StateHash(string stateName)
        {
            return Animator.StringToHash("Base Layer." + stateName);
        }
    }
}
