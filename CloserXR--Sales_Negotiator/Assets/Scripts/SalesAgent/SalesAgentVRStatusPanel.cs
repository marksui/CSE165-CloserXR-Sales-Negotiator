using UnityEngine;
using UnityEngine.UI;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesAgentVRStatusPanel : MonoBehaviour
    {
        [SerializeField] private bool showPanel = true;
        [SerializeField] private SalesConversationManager conversationManager;
        [SerializeField] private PushToTalkSpeechInput speechInput;
        [SerializeField] private GeminiSalesClient geminiClient;
        [SerializeField] private SpatialRoomMapDemo roomMap;
        [SerializeField] private Transform userHead;
        [SerializeField] private Vector3 headRelativeOffset = new Vector3(-0.42f, -0.2f, 1.25f);
        [SerializeField] private Vector2 panelSize = new Vector2(520f, 360f);
        [SerializeField] private float panelScale = 0.00115f;
        [SerializeField] private float followSharpness = 16f;
        [SerializeField] private int maxLineCharacters = 130;

        private Canvas canvas;
        private RectTransform panelRect;
        private Image statusRail;
        private Text statusText;
        private Text conversationText;
        private bool built;

        private static readonly Color PanelColor = new Color(0.04f, 0.07f, 0.12f, 0.82f);
        private static readonly Color BorderColor = new Color(0.28f, 0.36f, 0.48f, 0.85f);
        private static readonly Color HeaderColor = new Color(0.92f, 0.98f, 1f, 1f);
        private static readonly Color BodyColor = new Color(0.78f, 0.86f, 0.92f, 1f);
        private static readonly Color MutedColor = new Color(0.55f, 0.64f, 0.72f, 1f);
        private static readonly Color ReadyColor = new Color(0.13f, 0.78f, 0.37f, 1f);
        private static readonly Color BusyColor = new Color(0.96f, 0.69f, 0.18f, 1f);
        private static readonly Color FallbackColor = new Color(0.28f, 0.58f, 1f, 1f);

        private void Awake()
        {
            AutoWire();
        }

        public void Assign(
            SalesConversationManager conversation,
            PushToTalkSpeechInput input,
            GeminiSalesClient gemini,
            SpatialRoomMapDemo spatialMap,
            Transform head)
        {
            conversationManager = conversation;
            speechInput = input;
            geminiClient = gemini;
            roomMap = spatialMap;
            userHead = head;
        }

        private void LateUpdate()
        {
            if (!built)
            {
                BuildPanel();
            }

            if (canvas == null)
            {
                return;
            }

            canvas.gameObject.SetActive(showPanel);
            if (!showPanel)
            {
                return;
            }

            UpdatePose();
            UpdateText();
        }

        private void AutoWire()
        {
            conversationManager = conversationManager != null ? conversationManager : GetComponent<SalesConversationManager>();
            speechInput = speechInput != null ? speechInput : GetComponent<PushToTalkSpeechInput>();
            geminiClient = geminiClient != null ? geminiClient : GetComponent<GeminiSalesClient>();
            roomMap = roomMap != null ? roomMap : GetComponent<SpatialRoomMapDemo>();
            userHead = userHead != null ? userHead : Camera.main != null ? Camera.main.transform : null;
        }

        private void BuildPanel()
        {
            built = true;

            GameObject canvasObject = new GameObject("CloserXR VR Status Panel", typeof(RectTransform));
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 12f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            if (canvasRect == null)
            {
                canvasRect = canvasObject.AddComponent<RectTransform>();
            }

            canvasRect.sizeDelta = panelSize;
            canvasRect.localScale = Vector3.one * panelScale;

            GameObject panelObject = CreateChild(canvasObject.transform, "Panel");
            panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = false;

            Outline outline = panelObject.AddComponent<Outline>();
            outline.effectColor = BorderColor;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            statusRail = CreateBlock(panelRect, "Status Rail", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -18f), new Vector2(8f, 86f), ReadyColor);

            Text title = CreateText(panelRect, "Title", 26, FontStyle.Bold, HeaderColor, TextAnchor.UpperLeft);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -16f), new Vector2(466f, 38f));
            title.text = "CloserXR  |  Sales Negotiator";

            Text controls = CreateText(panelRect, "Controls", 20, FontStyle.Normal, BodyColor, TextAnchor.UpperLeft);
            SetRect(controls.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -58f), new Vector2(466f, 76f));
            controls.text =
                "Trigger  Hold to talk\n" +
                "A Product     B Price objection     X Proof     Y Accept\n" +
                "Right stick  Competitors / Reject / Contract / Maybe";

            Text statusHeader = CreateText(panelRect, "Status Header", 18, FontStyle.Bold, MutedColor, TextAnchor.UpperLeft);
            SetRect(statusHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -146f), new Vector2(220f, 28f));
            statusHeader.text = "STATUS";

            statusText = CreateText(panelRect, "Status Text", 20, FontStyle.Normal, BodyColor, TextAnchor.UpperLeft);
            SetRect(statusText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -174f), new Vector2(220f, 150f));

            Text conversationHeader = CreateText(panelRect, "Conversation Header", 18, FontStyle.Bold, MutedColor, TextAnchor.UpperLeft);
            SetRect(conversationHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(270f, -146f), new Vector2(230f, 28f));
            conversationHeader.text = "CONVERSATION";

            conversationText = CreateText(panelRect, "Conversation Text", 20, FontStyle.Normal, BodyColor, TextAnchor.UpperLeft);
            SetRect(conversationText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(270f, -174f), new Vector2(230f, 150f));
        }

        private void UpdatePose()
        {
            if (userHead == null && Camera.main != null)
            {
                userHead = Camera.main.transform;
            }

            if (userHead == null)
            {
                return;
            }

            Transform panel = canvas.transform;
            Vector3 targetPosition = userHead.TransformPoint(headRelativeOffset);
            Quaternion targetRotation = Quaternion.LookRotation(targetPosition - userHead.position, userHead.up);
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);

            panel.position = Vector3.Lerp(panel.position, targetPosition, t);
            panel.rotation = Quaternion.Slerp(panel.rotation, targetRotation, t);
        }

        private void UpdateText()
        {
            AutoWire();

            string agentStatus = conversationManager != null ? conversationManager.Status : "Booting";
            bool hasGeminiKey = conversationManager != null
                ? conversationManager.HasGeminiKey
                : geminiClient != null && geminiClient.HasApiKey;
            bool recording = speechInput != null && speechInput.IsRecording;
            bool busy = conversationManager != null && conversationManager.IsBusy;

            Color railColor = recording || busy ? BusyColor : hasGeminiKey ? ReadyColor : FallbackColor;
            if (statusRail != null)
            {
                statusRail.color = railColor;
            }

            string micStatus = recording ? "Recording" : "Idle";
            string geminiStatus = hasGeminiKey ? "Connected" : "Local fallback";
            string roomStatus = roomMap != null && roomMap.HasRoomBounds ? roomMap.BoundarySourceLabel : "Searching";

            statusText.text =
                $"Agent: {agentStatus}\n" +
                $"Gemini: {geminiStatus}\n" +
                $"Mic: {micStatus}\n" +
                $"Room: {roomStatus}\n" +
                "Mode: Passthrough MR";

            string userText = conversationManager != null ? conversationManager.LastUserText : "";
            string agentText = conversationManager != null ? conversationManager.LastAgentText : "";
            conversationText.text =
                $"User: {Compact(userText)}\n\n" +
                $"Agent: {Compact(agentText)}";
        }

        private string Compact(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            value = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return value.Length <= maxLineCharacters ? value : value.Substring(0, maxLineCharacters - 3) + "...";
        }

        private static Text CreateText(Transform parent, string name, int fontSize, FontStyle style, Color color, TextAnchor alignment)
        {
            GameObject textObject = CreateChild(parent, name);
            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateBlock(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            GameObject blockObject = CreateChild(parent, name);
            Image image = blockObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            SetRect(blockObject.GetComponent<RectTransform>(), anchorMin, anchorMax, anchoredPosition, size);
            return image;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private void OnDestroy()
        {
            if (canvas != null)
            {
                Destroy(canvas.gameObject);
            }
        }
    }
}
