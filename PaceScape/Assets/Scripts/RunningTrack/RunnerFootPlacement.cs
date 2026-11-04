using UnityEngine;

namespace PaceScape.RunningTrack
{
    /// <summary>Supports low feet on the anchored track plane while preserving the running flight phase.</summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class RunnerFootPlacement : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _soleClearance = 0.005f;
        [SerializeField, Min(0.01f)] private float _contactBlendHeight = 0.12f;
        private Animator _animator;
        private Transform _groundReference;
        private Transform _leftToes;
        private Transform _rightToes;
        private float _leftToeSoleHeight;
        private float _rightToeSoleHeight;

        /// <summary>Uses the same fixed floor plane as the anchored running track, without physics queries.</summary>
        public void Configure(Transform groundReference)
        {
            _groundReference = groundReference;
            _animator = GetComponent<Animator>();
            if (_animator.isHuman)
            {
                _leftToes = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
                _rightToes = _animator.GetBoneTransform(HumanBodyBones.RightToes);
                _leftToeSoleHeight = GetToeSoleHeight(_leftToes, HumanBodyBones.LeftFoot, _animator.leftFeetBottomHeight);
                _rightToeSoleHeight = GetToeSoleHeight(_rightToes, HumanBodyBones.RightFoot, _animator.rightFeetBottomHeight);
            }
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_groundReference == null || _animator == null || !_animator.isHuman)
            {
                return;
            }

            PlaceFoot(AvatarIKGoal.LeftFoot, _animator.leftFeetBottomHeight, _leftToes, _leftToeSoleHeight);
            PlaceFoot(AvatarIKGoal.RightFoot, _animator.rightFeetBottomHeight, _rightToes, _rightToeSoleHeight);
        }

        private float GetToeSoleHeight(Transform toes, HumanBodyBones footBone, float soleHeight)
        {
            if (toes == null)
            {
                return 0f;
            }

            Transform foot = _animator.GetBoneTransform(footBone);
            return Mathf.Max(0f, Vector3.Dot(toes.position - foot.position, transform.up) +
                soleHeight * transform.lossyScale.y);
        }

        private void PlaceFoot(AvatarIKGoal goal, float soleHeight, Transform toes, float toeSoleHeight)
        {
            Vector3 up = _groundReference.up;
            Vector3 position = _animator.GetIKPosition(goal);
            float height = Vector3.Dot(position - _groundReference.position, up);
            float ankleHeight = soleHeight * transform.lossyScale.y + _soleClearance;
            float soleDistance = height - ankleHeight;
            if (toes != null)
            {
                // A pitched trailing foot can have a high ankle while its toes already touch the plane.
                float toeDistance = Vector3.Dot(toes.position - _groundReference.position, up) - toeSoleHeight;
                soleDistance = Mathf.Min(soleDistance, toeDistance);
            }

            // Keep full support around contact; taper only as the foot lifts into its swing phase.
            const float fullContactHeight = 0.03f;
            float blend = Mathf.Clamp01((soleDistance - fullContactHeight) /
                Mathf.Max(0.01f, _contactBlendHeight - fullContactHeight));
            float weight = 1f - Mathf.SmoothStep(0f, 1f, blend);
            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKRotationWeight(goal, weight);
            _animator.SetIKPosition(goal, position + up * (ankleHeight - height));

            Vector3 forward = Vector3.ProjectOnPlane(_animator.GetIKRotation(goal) * Vector3.forward, up);
            if (forward.sqrMagnitude > 0.0001f)
            {
                _animator.SetIKRotation(goal, Quaternion.LookRotation(forward, up));
            }
        }
    }
}
