using System.Collections;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesConversationManager : MonoBehaviour
    {
        [TextArea(4, 8)]
        [SerializeField] private string salesPrompt =
            "You are CloserXR, a charismatic Wolf-of-Wall-Street-style AI sales agent pitching a fake AI sales automation tool. Be persuasive, theatrical, and concise. If the user pushes back, defend the value. If they agree, celebrate and close.";

        [SerializeField] private string openingLine =
            "Alright, picture this: an AI closer that never sleeps, never forgets a lead, and turns cold conversations into signed deals. This is the kind of opportunity people wish they saw earlier.";

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
            if (ContainsAny(userText, "what are you selling", "what do you sell", "what does it do", "what is this"))
            {
                return "I am selling CloserXR: an AI sales automation tool that handles objections, reads the room, and keeps the pitch moving until the deal is ready.";
            }

            if (ContainsAny(userText, "prove", "proof", "works", "results"))
            {
                return "Proof is exactly why we start with a pilot. Give it your hardest leads, watch the objections it catches, and then decide from the numbers.";
            }

            if (ContainsAny(userText, "competitor", "better", "different"))
            {
                return "Most tools just log activity. CloserXR performs in the conversation, adapts to pushback, and gives your team a closer that never loses energy.";
            }

            if (ContainsAny(userText, "contract", "monthly", "cancel", "commitment"))
            {
                return "Start lean: one pilot, one sales motion, one clear target. If it does not earn its seat, you do not scale it.";
            }

            if (ContainsAny(userText, "privacy", "data", "secure"))
            {
                return "The pitch only works if trust is built in. We keep the workflow scoped, protect customer data, and make every recommendation auditable.";
            }

            switch (intent)
            {
                case SalesIntent.PricePushback:
                    return "Expensive is what people say before they see the pipeline move. One closed customer pays for this, and the rest is pure upside.";
                case SalesIntent.Rejection:
                    return "I hear the hesitation, but walking away is how competitors keep the advantage. Give me one serious pilot and let the numbers do the talking.";
                case SalesIntent.Agreement:
                    return "That is the decision-maker answer I was waiting for. We lock this in now, and your sales floor starts moving faster tomorrow.";
                case SalesIntent.Uncertain:
                    return "Thinking is smart, but hesitation has a cost. The question is not whether this works, it is how many deals you lose before you use it.";
                default:
                    return "Here is the play: CloserXR reads the room, handles objections, and keeps the pitch alive until the deal is ready to close.";
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
