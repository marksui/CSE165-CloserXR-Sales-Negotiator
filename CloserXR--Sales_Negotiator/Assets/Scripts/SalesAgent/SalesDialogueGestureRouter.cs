using System;
using System.Collections;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesDialogueGestureRouter : MonoBehaviour
    {
        [SerializeField] private SalesAgentAnimator agentAnimator;
        [SerializeField] private bool talkDuringAgentLines = true;

        [Header("User Pushback")]
        [SerializeField] private string[] pricePushbackKeywords =
        {
            "too expensive",
            "price",
            "cost",
            "overpriced",
            "premium",
            "can't afford",
            "cannot afford",
            "budget"
        };

        [SerializeField] private string[] rejectionKeywords =
        {
            "no",
            "not interested",
            "don't want",
            "do not want",
            "pass",
            "stop"
        };

        [SerializeField] private string[] agreementKeywords =
        {
            "yes",
            "deal",
            "i'm in",
            "im in",
            "move forward",
            "apply",
            "sounds good",
            "sign me up"
        };

        [SerializeField] private string[] uncertaintyKeywords =
        {
            "maybe",
            "not sure",
            "think about it",
            "let me think",
            "hmm"
        };

        [Header("Agent Emphasis")]
        [SerializeField] private string[] closingKeywords =
        {
            "limited time",
            "right now",
            "close",
            "deal",
            "guarantee",
            "opportunity",
            "protection",
            "protect your family",
            "beneficiary",
            "application"
        };

        private void Reset()
        {
            agentAnimator = GetComponent<SalesAgentAnimator>();
        }

        public void AssignTarget(SalesAgentAnimator target)
        {
            agentAnimator = target;
        }

        public void AgentStartedSpeaking()
        {
            if (agentAnimator != null)
            {
                agentAnimator.SetTalking(true);
            }
        }

        public void AgentStoppedSpeaking()
        {
            if (agentAnimator != null)
            {
                agentAnimator.SetTalking(false);
            }
        }

        public void RouteUserText(string transcript)
        {
            if (agentAnimator == null || string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            if (ContainsAny(transcript, agreementKeywords))
            {
                agentAnimator.Celebrate();
                return;
            }

            if (ContainsAny(transcript, pricePushbackKeywords))
            {
                agentAnimator.Argue();
                return;
            }

            if (ContainsAny(transcript, rejectionKeywords))
            {
                agentAnimator.Dismiss();
                return;
            }

            if (ContainsAny(transcript, uncertaintyKeywords))
            {
                agentAnimator.Sad();
            }
        }

        public void RouteAgentText(string responseText, float delaySeconds = 0f)
        {
            if (agentAnimator == null || string.IsNullOrWhiteSpace(responseText))
            {
                return;
            }

            if (talkDuringAgentLines)
            {
                agentAnimator.SetTalking(true);
            }

            if (ContainsAny(responseText, closingKeywords))
            {
                if (delaySeconds > 0f)
                {
                    StartCoroutine(DelayedGesture(delaySeconds, agentAnimator.Point));
                }
                else
                {
                    agentAnimator.Point();
                }
            }
        }

        private static IEnumerator DelayedGesture(float delay, Action gesture)
        {
            yield return new WaitForSeconds(delay);
            gesture?.Invoke();
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
