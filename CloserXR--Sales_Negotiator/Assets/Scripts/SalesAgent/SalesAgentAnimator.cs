using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    public enum SalesAgentGesture
    {
        Point,
        Argue,
        Dismiss,
        Celebrate,
        Sad
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class SalesAgentAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        [Header("Animator Parameters")]
        [SerializeField] private string talkingBool = "IsTalking";
        [SerializeField] private string walkingBool = "IsWalking";
        [SerializeField] private string pointTrigger = "Point";
        [SerializeField] private string argueTrigger = "Argue";
        [SerializeField] private string dismissTrigger = "Dismiss";
        [SerializeField] private string celebrateTrigger = "Celebrate";
        [SerializeField] private string sadTrigger = "Sad";
        [SerializeField] private string resetTrigger = "Reset";

        private int talkingHash;
        private int walkingHash;
        private int pointHash;
        private int argueHash;
        private int dismissHash;
        private int celebrateHash;
        private int sadHash;
        private int resetHash;

        private void Reset()
        {
            animator = GetComponent<Animator>();
            CacheHashes();
        }

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            CacheHashes();
        }

        private void OnValidate()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            CacheHashes();
        }

        public void AssignAnimator(Animator value)
        {
            animator = value;
            CacheHashes();
        }

        public void SetTalking(bool isTalking)
        {
            SetBool(talkingHash, AnimatorControllerParameterType.Bool, isTalking);

            if (isTalking)
            {
                SetBool(walkingHash, AnimatorControllerParameterType.Bool, false);
            }
        }

        public void SetWalking(bool isWalking)
        {
            SetBool(walkingHash, AnimatorControllerParameterType.Bool, isWalking);

            if (isWalking)
            {
                SetBool(talkingHash, AnimatorControllerParameterType.Bool, false);
            }
        }

        public void PlayGesture(SalesAgentGesture gesture)
        {
            switch (gesture)
            {
                case SalesAgentGesture.Point:
                    Point();
                    break;
                case SalesAgentGesture.Argue:
                    Argue();
                    break;
                case SalesAgentGesture.Dismiss:
                    Dismiss();
                    break;
                case SalesAgentGesture.Celebrate:
                    Celebrate();
                    break;
                case SalesAgentGesture.Sad:
                    Sad();
                    break;
            }
        }

        public void Point()
        {
            SetTrigger(pointHash);
        }

        public void Argue()
        {
            SetTrigger(argueHash);
        }

        public void Dismiss()
        {
            SetTrigger(dismissHash);
        }

        public void Celebrate()
        {
            SetTrigger(celebrateHash);
        }

        public void Sad()
        {
            SetBool(talkingHash, AnimatorControllerParameterType.Bool, false);
            SetBool(walkingHash, AnimatorControllerParameterType.Bool, false);
            SetTrigger(sadHash);
        }

        public void ResetToIdle()
        {
            SetBool(talkingHash, AnimatorControllerParameterType.Bool, false);
            SetBool(walkingHash, AnimatorControllerParameterType.Bool, false);
            SetTrigger(resetHash);
        }

        // Modulate the animator's global speed while the agent is talking to produce
        // organic lip-variation without requiring real phoneme data.
        public void SetTalkingSpeed(float speed)
        {
            if (animator != null)
            {
                animator.speed = Mathf.Clamp(speed, 0.5f, 2f);
            }
        }

        private void CacheHashes()
        {
            talkingHash = Animator.StringToHash(talkingBool);
            walkingHash = Animator.StringToHash(walkingBool);
            pointHash = Animator.StringToHash(pointTrigger);
            argueHash = Animator.StringToHash(argueTrigger);
            dismissHash = Animator.StringToHash(dismissTrigger);
            celebrateHash = Animator.StringToHash(celebrateTrigger);
            sadHash = Animator.StringToHash(sadTrigger);
            resetHash = Animator.StringToHash(resetTrigger);
        }

        private void SetBool(int parameterHash, AnimatorControllerParameterType expectedType, bool value)
        {
            if (!CanUseParameter(parameterHash, expectedType))
            {
                return;
            }

            animator.SetBool(parameterHash, value);
        }

        private void SetTrigger(int parameterHash)
        {
            if (!CanUseParameter(parameterHash, AnimatorControllerParameterType.Trigger))
            {
                return;
            }

            animator.SetTrigger(parameterHash);
        }

        private bool CanUseParameter(int parameterHash, AnimatorControllerParameterType expectedType)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == parameterHash && parameter.type == expectedType)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
