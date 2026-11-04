using System;
using UnityEngine;

namespace PaceScape.RunningTrack
{
    /// <summary>Moves one grounded object along its lane, smoothing live speed changes with its animation.</summary>
    public sealed class RunningTrackMotion
    {
        private static readonly int _speedParameter = Animator.StringToHash("Speed");
        private static readonly int _playbackRateParameter = Animator.StringToHash("PlaybackRate");
        private const double SpeedTransitionSeconds = 0.35;
        private readonly Transform _target;
        private readonly Animator _animator;
        private readonly int _lane;
        private readonly float _groundOffset;
        private bool _hasCheckedAnimationParameters;
        private bool _hasSpeedParameter;
        private bool _hasPlaybackRate;
        private float _walkSpeed;
        private float _sprintSpeed;
        private bool _hasAdvanced;
        private double _speed;
        private double _distance;

        /// <summary>Places the object behind the shared start line, preserving its grounded height and scale.</summary>
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
            _animator = target.GetComponentInChildren<Animator>(true);
            _lane = lane;
            _groundOffset = target.localPosition.y;
            _distance = -distanceBehindStartLine % RunningTrackGeometry.GetLaneLength(lane);
            ApplyPose();
        }

        /// <summary>Advances by elapsed visible time; invalid or nonpositive time and pace leave the object still.</summary>
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
            _distance = (_distance + travel) % RunningTrackGeometry.GetLaneLength(_lane);
            ApplyPose();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

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
            Pose pose = RunningTrackGeometry.EvaluateLane(_lane, _distance);
            _target.SetLocalPositionAndRotation(pose.position + Vector3.up * _groundOffset, pose.rotation);
        }
    }
}
