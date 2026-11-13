// Moves the runner along its lane and matches the animation to its physical speed.

using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    // Moves one grounded object along its lane, smoothing live speed changes with its animation.
    public sealed class RunningTrackMotion
    {
        private static readonly int _speedParameter = Animator.StringToHash("Speed");
        private static readonly int _playbackRateParameter = Animator.StringToHash("PlaybackRate");
        private const double SpeedTransitionSeconds = 0.35;
        private const float JogBlendStartFactor = 0.85f;
        private readonly Transform _target;
        private readonly Animator _animator;
        private int _lane;
        private float _distanceBehindStartLine;
        private readonly float _groundOffset;
        private bool _hasCheckedAnimationParameters;
        private bool _hasSpeedParameter;
        private bool _hasPlaybackRate;
        private float _walkSpeed;
        private float _runSpeed;
        private float _sprintSpeed;
        private bool _hasAdvanced;
        private double _speed;
        private double _distance;
        private readonly Renderer[] _renderers;
        private readonly bool[] _rendererVisibility;
        private bool _hasRelativePose;

        // Gets unwrapped travel from the configured start, excluding the start offset.
        public double TravelledMetres { get; private set; }

        // Places the object behind the shared start line, preserving its grounded height and scale.
        public RunningTrackMotion(Transform target, int lane, float distanceBehindStartLine)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (lane < 1 || lane > RunningTrackGeometry.LaneCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lane));
            }

            if (float.IsNaN(distanceBehindStartLine) || float.IsInfinity(distanceBehindStartLine) || distanceBehindStartLine < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(distanceBehindStartLine));
            }

            Vector3 position = target.localPosition;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
            {
                throw new ArgumentException("The runner requires a finite local position.", nameof(target));
            }

            _target = target;
            _renderers = target.GetComponentsInChildren<Renderer>(true);
            _rendererVisibility = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _rendererVisibility[i] = _renderers[i].enabled;
            }
            _animator = target.GetComponentInChildren<Animator>(true);
            _groundOffset = target.localPosition.y;
            ConfigureStart(lane, distanceBehindStartLine);
        }

        // Repositions on the requested lane at the start offset while preserving grounded height.
        public void ConfigureStart(int lane, float distanceBehindStartLine)
        {
            if (lane < 1 || lane > RunningTrackGeometry.LaneCount || !IsFinite(distanceBehindStartLine) ||
                distanceBehindStartLine < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(distanceBehindStartLine));
            }

            _lane = lane;
            _distanceBehindStartLine = distanceBehindStartLine;
            ResetToStart();
        }

        // Returns to the configured start without moving the anchor or track.
        public void ResetToStart()
        {
            TravelledMetres = 0;
            _hasAdvanced = false;
            _speed = 0;
            _distance = -_distanceBehindStartLine % RunningTrackGeometry.GetLaneLength(_lane);
            UpdateAnimation(0f);
            ApplyPose();
        }

        // Advances by elapsed visible time; invalid or nonpositive time and pace leave the object still.
        public void Advance(float deltaTime, float paceMinutesPerKilometre)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0f)
            {
                return;
            }

            if (!IsFinite(paceMinutesPerKilometre) || paceMinutesPerKilometre <= 0f)
            {
                _speed = 0;
                _hasAdvanced = false;
                UpdateAnimation(0f);
                return;
            }

            if (_target == null)
            {
                throw new InvalidOperationException("The runner object was removed.");
            }

            double targetSpeed = 1000.0 / ((double)paceMinutesPerKilometre * 60.0);
            if (!_hasAdvanced)
            {
                _speed = targetSpeed;
                _hasAdvanced = true;
            }

            // Integrate the speed response exactly so travel does not depend on the frame rate.
            double decay = Math.Exp(-deltaTime / SpeedTransitionSeconds);
            double difference = _speed - targetSpeed;
            double travel = targetSpeed * deltaTime + difference * SpeedTransitionSeconds * (1.0 - decay);
            _speed = targetSpeed + difference * decay;
            UpdateAnimation((float)Math.Min(_speed, float.MaxValue));
            TravelledMetres += travel;
            _distance = (_distance + travel) % RunningTrackGeometry.GetLaneLength(_lane);
            ApplyPose();
        }

        // Uses exact recorded travel while smoothing only the visual gait response.
        public void AdvanceRecorded(float deltaTime, double travelMetres, double endSpeedMetresPerSecond)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0 || double.IsNaN(travelMetres) ||
                double.IsInfinity(travelMetres) || travelMetres < 0 || double.IsNaN(endSpeedMetresPerSecond) ||
                double.IsInfinity(endSpeedMetresPerSecond) || endSpeedMetresPerSecond < 0)
            {
                return;
            }
            if (_target == null)
            {
                throw new InvalidOperationException("The runner object was removed.");
            }
            _speed = _hasAdvanced ? endSpeedMetresPerSecond + (_speed - endSpeedMetresPerSecond) *
                Math.Exp(-deltaTime / SpeedTransitionSeconds) : endSpeedMetresPerSecond;
            _hasAdvanced = true;
            UpdateAnimation((float)Math.Min(_speed, float.MaxValue));
            TravelledMetres += travelMetres;
            _distance = (_distance + travelMetres) % RunningTrackGeometry.GetLaneLength(_lane);
            ApplyPose();
        }

        // Overrides only visible placement; logical lane travel and gait speed stay independent.
        public void SetPresentationPose(Pose localFloorPose, bool visible)
        {
            _hasRelativePose = true;
            _target.SetLocalPositionAndRotation(localFloorPose.position + Vector3.up * _groundOffset,
                localFloorPose.rotation);
            SetRendererVisibility(visible);
        }

        // Restores the selected lane at its current logical progress, including initial renderer flags.
        public void ClearPresentationPose()
        {
            if (!_hasRelativePose)
            {
                return;
            }
            _hasRelativePose = false;
            SetRendererVisibility(true);
            ApplyPose();
        }

        // Hides presentation renderers and restores their original flags when visible.
        private void SetRendererVisibility(bool visible)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = visible && _rendererVisibility[i];
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        // Maps physical speed to gait blending and playback rate.
        private void UpdateAnimation(float speed)
        {
            if (_animator == null || !_animator.isActiveAndEnabled || !_animator.isInitialized)
            {
                return;
            }

            if (!_hasCheckedAnimationParameters)
            {
                // Marker content starts inactive, when Unity exposes no Animator parameters yet.
                foreach (AnimatorControllerParameter parameter in _animator.parameters)
                {
                    if (parameter.nameHash == _speedParameter && parameter.type == AnimatorControllerParameterType.Float)
                    {
                        _hasSpeedParameter = true;
                        // Starter Assets clips include footstep callbacks; this visual has no audio receiver.
                        _animator.fireEvents = false;
                    }

                    _hasPlaybackRate |= parameter.nameHash == _playbackRateParameter &&
                        parameter.type == AnimatorControllerParameterType.Float;
                }

                if (_hasPlaybackRate)
                {
                    foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
                    {
                        float nativeSpeed = new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude;
                        if (clip.name == "walking")
                        {
                            _walkSpeed = nativeSpeed;
                        }
                        else if (clip.name == "runninig" || clip.name == "running")
                        {
                            _runSpeed = nativeSpeed;
                        }
                        else if (clip.name == "sprint")
                        {
                            _sprintSpeed = nativeSpeed;
                        }
                    }
                }

                _hasCheckedAnimationParameters = true;
            }

            if (_hasSpeedParameter)
            {
                // Match the unscaled movement clock without changing the configured physical pace.
                float blendSpeed = speed;
                float playbackRate = 1f;
                if (_hasPlaybackRate && _walkSpeed > 0f && _sprintSpeed > _walkSpeed && speed > 0f)
                {
                    // Native root velocities calibrate cadence; only the track owns world displacement.
                    blendSpeed = Mathf.Clamp(speed, _walkSpeed, _sprintSpeed);
                    if (_runSpeed > _walkSpeed && speed < _runSpeed)
                    {
                        // Introduce a small jog contribution earlier without changing path speed.
                        float jogWeight = Mathf.InverseLerp(_walkSpeed * JogBlendStartFactor, _runSpeed, speed);
                        blendSpeed = Mathf.Lerp(_walkSpeed, _runSpeed, jogWeight);
                    }

                    // Compensate for the blended clip's native velocity, including the early jog mixture.
                    playbackRate = speed / blendSpeed;
                }

                _animator.SetFloat(_speedParameter, blendSpeed);
                if (_hasPlaybackRate)
                {
                    _animator.SetFloat(_playbackRateParameter, playbackRate);
                }
            }
        }

        private void ApplyPose()
        {
            if (_hasRelativePose)
            {
                return;
            }
            Pose pose = RunningTrackGeometry.EvaluateLane(_lane, _distance);
            _target.SetLocalPositionAndRotation(pose.position + Vector3.up * _groundOffset, pose.rotation);
        }
    }
}
