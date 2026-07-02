using UnityEngine;
using Vapor.Inspector;

namespace Vapor
{
    [ExecuteInEditMode]
    public class AnimationPoser : VaporBehaviour
    {
        [SerializeField] private AnimationClip _animationClip;
        [SerializeField] private Animator _animator;
        [SerializeField, Range(0f, 1f)] private float _normalizedTime;
        [SerializeField] private bool _applyInEditMode = true;

        private float _lastNormalizedTime = -1f;
        private AnimationClip _lastAnimationClip;

        private void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && _applyInEditMode)
            {
                if (_animationClip != _lastAnimationClip || !Mathf.Approximately(_normalizedTime, _lastNormalizedTime))
                {
                    ApplyPose();
                    _lastNormalizedTime = _normalizedTime;
                    _lastAnimationClip = _animationClip;
                }
            }
#endif
        }

        [Button("Apply Pose")]
        public void ApplyPose()
        {
            if (_animationClip == null || _animator == null)
            {
                Debug.LogWarning("AnimationClip or Animator is not assigned.");
                return;
            }

            if (!_animator.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("Animator GameObject must be active to apply pose.");
                return;
            }

            _animationClip.SampleAnimation(_animator.gameObject, _normalizedTime * _animationClip.length);
        }
    }
}
