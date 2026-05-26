using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesAgentDebugControls : MonoBehaviour
    {
        [SerializeField] private SalesAgentAnimator agentAnimator;

        private void Reset()
        {
            agentAnimator = GetComponent<SalesAgentAnimator>();
        }

        public void AssignTarget(SalesAgentAnimator target)
        {
            agentAnimator = target;
        }

        private void Update()
        {
            if (agentAnimator == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                agentAnimator.SetTalking(true);
            }

            if (Input.GetKeyDown(KeyCode.Y))
            {
                agentAnimator.SetTalking(false);
            }

            if (Input.GetKeyDown(KeyCode.W))
            {
                agentAnimator.SetWalking(true);
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                agentAnimator.SetWalking(false);
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                agentAnimator.Point();
            }

            if (Input.GetKeyDown(KeyCode.A))
            {
                agentAnimator.Argue();
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                agentAnimator.Dismiss();
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                agentAnimator.Celebrate();
            }

            if (Input.GetKeyDown(KeyCode.S))
            {
                agentAnimator.Sad();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                agentAnimator.ResetToIdle();
            }
        }
    }
}
