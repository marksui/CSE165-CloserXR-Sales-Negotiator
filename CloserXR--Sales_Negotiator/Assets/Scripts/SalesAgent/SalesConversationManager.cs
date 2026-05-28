using System.Collections;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesConversationManager : MonoBehaviour
    {
        [TextArea(4, 8)]
        [SerializeField] private string salesPrompt =
            "You are CloserXR, a charismatic but ethical life insurance sales agent in a VR role-play demo. Sell life insurance as family protection. Be persuasive, concise, and responsive. Explain term and whole life at a high level, handle premium concerns, and close when the user agrees. Do not give real financial, legal, or insurance advice; recommend a licensed advisor for actual decisions.";

        [SerializeField] private string openingLine =
            "Alright, picture this: if something happened tomorrow, your family would still have money for the mortgage, tuition, and everyday life. This policy is about protecting the people who count on you.";

        [SerializeField] private bool startWithOpeningPitch = true;

        private GeminiSalesClient geminiClient;
        private SalesDialogueGestureRouter gestureRouter;
        private SalesAgentAnimator agentAnimator;
        private SalesAgentPacer pacer;
        private Coroutine speakingRoutine;
        private bool waitingForGemini;

        public string LastUserText { get; private set; } = "";
        public string LastAgentText { get; private set; } = "";
        public string Status { get; private set; } = "Ready";
        public bool IsBusy { get; private set; }

        public bool HasGeminiKey => geminiClient != null && geminiClient.HasApiKey;

        private void Awake()
        {
            AutoWire();
        }

        private void Start()
        {
            if (startWithOpeningPitch && string.IsNullOrWhiteSpace(LastAgentText))
            {
                DeliverAgentResponse(openingLine);
            }
        }

        public void Configure(
            GeminiSalesClient gemini,
            SalesDialogueGestureRouter router,
            SalesAgentAnimator animator,
            SalesAgentPacer agentPacer)
        {
            geminiClient = gemini;
            gestureRouter = router;
            agentAnimator = animator;
            pacer = agentPacer;
        }

        public void SubmitUserText(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
            {
                return;
            }

            if (IsBusy)
            {
                if (waitingForGemini)
                {
                    return;
                }

                StopCurrentSpeech();
            }

            LastUserText = userText.Trim();
            SalesIntent userIntent = SalesIntentClassifier.ClassifyUserText(LastUserText);
            gestureRouter?.RouteUserText(LastUserText);
            pacer?.SetIntent(userIntent);

            if (geminiClient != null && geminiClient.HasApiKey)
            {
                StartCoroutine(GenerateGeminiTextReply(LastUserText));
                return;
            }

            DeliverAgentResponse(BuildLocalReply(userIntent, LastUserText));
        }

        public void SubmitUserAudio(AudioClip clip)
        {
            if (IsBusy || clip == null)
            {
                return;
            }

            if (geminiClient == null || !geminiClient.HasApiKey)
            {
                Status = "Audio recorded. Add GEMINI_API_KEY to let Gemini understand microphone input.";
                DeliverAgentResponse(BuildLocalReply(SalesIntent.Neutral, LastUserText));
                return;
            }

            LastUserText = "[microphone audio]";
            StartCoroutine(GenerateGeminiAudioReply(clip));
        }

        private IEnumerator GenerateGeminiTextReply(string userText)
        {
            IsBusy = true;
            waitingForGemini = true;
            Status = "Asking Gemini...";
            bool completed = false;

            yield return geminiClient.GenerateFromText(
                userText,
                salesPrompt,
                response =>
                {
                    completed = true;
                    waitingForGemini = false;
                    DeliverAgentResponse(response);
                },
                error =>
                {
                    completed = true;
                    waitingForGemini = false;
                    Status = "Gemini failed; using local sales response.";
                    DeliverAgentResponse(BuildLocalReply(SalesIntentClassifier.ClassifyUserText(userText), userText));
                    Debug.LogWarning($"Gemini request failed: {error}");
                });

            if (!completed)
            {
                waitingForGemini = false;
                IsBusy = false;
            }
        }

        private IEnumerator GenerateGeminiAudioReply(AudioClip clip)
        {
            IsBusy = true;
            waitingForGemini = true;
            Status = "Sending audio to Gemini...";
            bool completed = false;

            yield return geminiClient.GenerateFromAudio(
                clip,
                salesPrompt,
                response =>
                {
                    completed = true;
                    waitingForGemini = false;
                    DeliverAgentResponse(response);
                },
                error =>
                {
                    completed = true;
                    waitingForGemini = false;
                    Status = "Gemini audio failed; using local sales response.";
                    DeliverAgentResponse(BuildLocalReply(SalesIntent.Neutral, LastUserText));
                    Debug.LogWarning($"Gemini audio request failed: {error}");
                });

            if (!completed)
            {
                waitingForGemini = false;
                IsBusy = false;
            }
        }

        private void DeliverAgentResponse(string response)
        {
            LastAgentText = response;
            Status = geminiClient != null && geminiClient.HasApiKey ? "Gemini response" : "Local demo response";

            SalesIntent agentIntent = SalesIntentClassifier.ClassifyAgentText(response);
            gestureRouter?.RouteAgentText(response);
            pacer?.SetIntent(agentIntent == SalesIntent.Closing ? SalesIntent.Closing : SalesIntent.Neutral);

            if (speakingRoutine != null)
            {
                StopCoroutine(speakingRoutine);
            }

            speakingRoutine = StartCoroutine(SimulateSpeaking(response));
        }

        private IEnumerator SimulateSpeaking(string response)
        {
            IsBusy = true;
            gestureRouter?.AgentStartedSpeaking();

            float seconds = Mathf.Clamp(response.Split(' ').Length * 0.22f, 2.2f, 8f);
            yield return new WaitForSeconds(seconds);

            gestureRouter?.AgentStoppedSpeaking();
            pacer?.GoIdle();
            Status = "Ready";
            IsBusy = false;
        }

        private void StopCurrentSpeech()
        {
            if (speakingRoutine != null)
            {
                StopCoroutine(speakingRoutine);
                speakingRoutine = null;
            }

            gestureRouter?.AgentStoppedSpeaking();
            pacer?.GoIdle();
            IsBusy = false;
        }

        private string BuildLocalReply(SalesIntent intent, string userText)
        {
            if (ContainsAny(userText, "what are you selling", "what do you sell", "what does it do", "what is this", "what kind of life insurance", "policy"))
            {
                return "I am selling life insurance: a policy that pays a benefit to your beneficiaries if you pass away, so your family has money for housing, debt, childcare, and final expenses.";
            }

            if (ContainsAny(userText, "prove", "proof", "works", "results", "protect", "family"))
            {
                return "The proof is in the coverage plan. We estimate income replacement, debts, mortgage, and final expenses, then compare the premium against what your family would need.";
            }

            if (ContainsAny(userText, "coverage", "how much", "beneficiary", "need"))
            {
                return "A good starting point is who depends on your income, how much debt you carry, and how many years of support they would need. For a real policy, a licensed advisor should check the numbers.";
            }

            if (ContainsAny(userText, "term", "whole", "permanent"))
            {
                return "Term life is usually lower-cost coverage for a set number of years. Whole life can last longer and may build cash value, but the premium is usually higher.";
            }

            if (ContainsAny(userText, "competitor", "better", "different"))
            {
                return "The better policy is the one that fits your family's risk, budget, and timeline. I would compare coverage amount, term length, riders, and premium before you decide.";
            }

            if (ContainsAny(userText, "contract", "monthly", "cancel", "commitment", "application"))
            {
                return "You review the application, premium, beneficiaries, and policy terms before anything is final. The smart move is understanding the coverage before you sign.";
            }

            if (ContainsAny(userText, "privacy", "data", "secure"))
            {
                return "Insurance uses sensitive personal and health information, so privacy matters. In the real world, that information should be handled only through secure, approved channels.";
            }

            switch (intent)
            {
                case SalesIntent.PricePushback:
                    return "I hear you. The premium feels expensive today, but the risk is leaving your family with the mortgage, bills, and final expenses tomorrow.";
                case SalesIntent.Rejection:
                    return "I hear the hesitation. Nobody likes thinking about life insurance, but protecting your family is easier to handle before there is an emergency.";
                case SalesIntent.Agreement:
                    return "Good decision. Next we review the coverage amount, beneficiaries, and application details with a licensed advisor so you know exactly what you are choosing.";
                case SalesIntent.Uncertain:
                    return "Thinking it through is smart. Let us make it simple: choose the coverage your family would actually need, then see whether the premium fits your budget.";
                default:
                    return "Here is the value: life insurance turns one monthly premium into protection for the people who rely on you most.";
            }
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (string keyword in keywords)
            {
                if (text.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void AutoWire()
        {
            geminiClient = geminiClient != null ? geminiClient : GetComponent<GeminiSalesClient>();
            gestureRouter = gestureRouter != null ? gestureRouter : GetComponent<SalesDialogueGestureRouter>();
            agentAnimator = agentAnimator != null ? agentAnimator : GetComponent<SalesAgentAnimator>();
            pacer = pacer != null ? pacer : GetComponent<SalesAgentPacer>();
        }
    }
}
