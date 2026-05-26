using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesConversationDebugHud : MonoBehaviour
    {
        [SerializeField] private SalesConversationManager conversationManager;
        [SerializeField] private PushToTalkSpeechInput speechInput;
        [SerializeField] private bool showHud = true;
        [SerializeField] private string[] quickUserLines =
        {
            "What are you selling?",
            "This is too expensive",
            "I'm not interested",
            "Can you prove it works?",
            "Maybe I need to think about it",
            "What makes this better than competitors?",
            "Do I have to sign a contract?",
            "Yes, deal, sign me up"
        };

        private string typedUserText = "This is too expensive";

        private void Awake()
        {
            if (conversationManager == null)
            {
                conversationManager = GetComponent<SalesConversationManager>();
            }

            if (speechInput == null)
            {
                speechInput = GetComponent<PushToTalkSpeechInput>();
            }
        }

        public void Assign(SalesConversationManager manager, PushToTalkSpeechInput input)
        {
            conversationManager = manager;
            speechInput = input;
        }

        private void OnGUI()
        {
            if (!showHud || conversationManager == null)
            {
                return;
            }

            const int width = 520;
            GUILayout.BeginArea(new Rect(18, 18, width, 470), GUI.skin.box);
            GUILayout.Label("CloserXR Sales Negotiator");
            GUILayout.Label($"Status: {conversationManager.Status}");
            GUILayout.Label($"Gemini: {(conversationManager.HasGeminiKey ? "connected" : "local fallback")}");
            GUILayout.Label($"Mic: {(speechInput != null && speechInput.IsRecording ? "recording" : "hold Space / Quest trigger")}");

            GUILayout.Space(8);
            GUILayout.Label($"User: {conversationManager.LastUserText}");
            GUILayout.Label($"Agent: {conversationManager.LastAgentText}");

            GUILayout.Space(8);
            GUI.SetNextControlName("CloserXRTextInput");
            typedUserText = GUILayout.TextField(typedUserText, GUILayout.Width(width - 20));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Send"))
            {
                conversationManager.SubmitUserText(typedUserText);
            }
            GUILayout.EndHorizontal();

            foreach (string quickUserLine in quickUserLines)
            {
                GUILayout.BeginHorizontal();
                AddQuickLineButton(quickUserLine);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();

            Event current = Event.current;
            if (current.isKey && current.type == EventType.KeyDown && current.keyCode == KeyCode.Return)
            {
                conversationManager.SubmitUserText(typedUserText);
            }
        }

        private void AddQuickLineButton(string line)
        {
            if (GUILayout.Button(line))
            {
                typedUserText = line;
                conversationManager.SubmitUserText(line);
            }
        }
    }
}
