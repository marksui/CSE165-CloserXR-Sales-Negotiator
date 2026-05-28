using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesConversationDebugHud : MonoBehaviour
    {
        [SerializeField] private SalesConversationManager conversationManager;
        [SerializeField] private PushToTalkSpeechInput speechInput;
        [SerializeField] private bool showHud = true;
        [SerializeField] private bool showCanvasHud;
        [SerializeField] private bool showLegacyOnGui = true;
        [SerializeField] private Vector2 screenMargin = new Vector2(12f, 12f);
        [SerializeField] private Vector2 panelSize = new Vector2(352f, 232f);

        private const int LegacyWidth = 460;
        private const int LegacyHeight = 360;
        private static readonly Color PanelColor = new Color(0.965f, 0.985f, 0.975f, 0.94f);
        private static readonly Color BorderColor = new Color(0.18f, 0.42f, 0.48f, 0.35f);
        private static readonly Color TitleColor = new Color(0.04f, 0.16f, 0.2f, 1f);
        private static readonly Color BodyColor = new Color(0.08f, 0.13f, 0.15f, 1f);
        private static readonly Color MutedBlueColor = new Color(0.14f, 0.35f, 0.5f, 1f);
        private static readonly Color ReadyColor = new Color(0.07f, 0.45f, 0.34f, 1f);
        private static readonly Color AgentColor = new Color(0.13f, 0.31f, 0.34f, 1f);
        private static readonly Color ButtonColor = new Color(0.08f, 0.43f, 0.48f, 1f);
        private static readonly Color ButtonHighlightColor = new Color(0.1f, 0.56f, 0.6f, 1f);
        private static readonly Color ButtonPressedColor = new Color(0.04f, 0.28f, 0.32f, 1f);

        private Canvas hudCanvas;
        private RectTransform panelRect;
        private Text statusText;
        private Text geminiText;
        private Text micText;
        private Text userText;
        private Text agentText;
        private InputField inputField;
        private Font hudFont;
        private string typedUserText = "The premium is too expensive";
        private readonly string[] quickUserLines =
        {
            "What kind of life insurance is this?",
            "The premium is too expensive",
            "I'm not interested",
            "How does this protect my family?",
            "Maybe I need to think about it",
            "How much coverage do I need?",
            "Is this term or whole life?",
            "I want to move forward"
        };

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

        private void OnEnable()
        {
            if (showHud && showCanvasHud)
            {
                EnsureHud();
            }
        }

        public void Assign(SalesConversationManager manager, PushToTalkSpeechInput input)
        {
            conversationManager = manager;
            speechInput = input;
            UpdateHudText();
        }

        private void LateUpdate()
        {
            if (!showHud || !showCanvasHud)
            {
                if (hudCanvas != null)
                {
                    hudCanvas.gameObject.SetActive(false);
                }

                return;
            }

            EnsureHud();
            BindCanvasToMainCamera();
            UpdateHudText();
        }

        private void EnsureHud()
        {
            if (hudCanvas != null)
            {
                hudCanvas.gameObject.SetActive(true);
                return;
            }

            hudFont = LoadBuiltinFont();
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("CloserXR Camera HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            hudCanvas = canvasObject.GetComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            hudCanvas.planeDistance = 1f;
            hudCanvas.sortingOrder = 1000;
            BindCanvasToMainCamera();

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            GraphicRaycaster raycaster = canvasObject.GetComponent<GraphicRaycaster>();
            raycaster.ignoreReversedGraphics = true;

            Image panel = CreatePanel(canvasObject.transform);
            panelRect = panel.GetComponent<RectTransform>();

            CreateBlock(panel.transform, "Header Tint", 0f, 0f, panelSize.x, 54f, new Color(0.82f, 0.93f, 0.94f, 0.72f));
            CreateBlock(panel.transform, "Conversation Tint", 10f, 90f, panelSize.x - 20f, 84f, new Color(1f, 1f, 1f, 0.58f));

            CreateStaticText(panel.transform, "CloserXR Sales Negotiator", 17, FontStyle.Bold, TitleColor, 14f, 8f, 320f, 24f);
            statusText = CreateStaticText(panel.transform, "", 12, FontStyle.Bold, BodyColor, 14f, 34f, 320f, 18f);
            geminiText = CreateStaticText(panel.transform, "", 11, FontStyle.Normal, MutedBlueColor, 14f, 57f, 320f, 16f);
            micText = CreateStaticText(panel.transform, "", 11, FontStyle.Normal, ReadyColor, 14f, 75f, 320f, 16f);
            userText = CreateStaticText(panel.transform, "", 11, FontStyle.Normal, BodyColor, 18f, 100f, 316f, 30f);
            agentText = CreateStaticText(panel.transform, "", 11, FontStyle.Normal, AgentColor, 18f, 134f, 316f, 38f);

            inputField = CreateInputField(panel.transform, 14f, 190f, 184f, 28f);
            CreateButton(panel.transform, "Send", 204f, 190f, 46f, 28f, SubmitTypedUserText);
            CreateButton(panel.transform, "Price", 256f, 190f, 42f, 28f, () => SubmitPreset("The premium is too expensive"));
            CreateButton(panel.transform, "Deal", 304f, 190f, 36f, 28f, () => SubmitPreset("I want to move forward"));

            UpdateHudText();
        }

        private Image CreatePanel(Transform parent)
        {
            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(parent, false);

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(screenMargin.x, -screenMargin.y);
            rect.sizeDelta = panelSize;

            Image image = panelObject.GetComponent<Image>();
            image.color = PanelColor;

            Outline outline = panelObject.AddComponent<Outline>();
            outline.effectColor = BorderColor;
            outline.effectDistance = new Vector2(1.25f, -1.25f);
            return image;
        }

        private void BindCanvasToMainCamera()
        {
            Camera mainCamera = Camera.main;
            if (hudCanvas == null || mainCamera == null)
            {
                return;
            }

            hudCanvas.worldCamera = mainCamera;
        }

        private void UpdateHudText()
        {
            if (statusText == null)
            {
                return;
            }

            if (panelRect != null)
            {
                panelRect.anchoredPosition = new Vector2(screenMargin.x, -screenMargin.y);
                panelRect.sizeDelta = panelSize;
            }

            if (conversationManager == null)
            {
                statusText.text = "Status: waiting for conversation";
                geminiText.text = "";
                micText.text = "";
                userText.text = "";
                agentText.text = "";
                return;
            }

            statusText.text = "Status: " + conversationManager.Status;
            geminiText.text = "Gemini: " + (conversationManager.HasGeminiKey ? "connected" : "local fallback");
            micText.text = "Mic: " + (speechInput != null && speechInput.IsRecording ? "recording" : "Space / Quest trigger");
            userText.text = "User: " + BlankFallback(conversationManager.LastUserText);
            agentText.text = "Agent: " + BlankFallback(conversationManager.LastAgentText);
        }

        private Text CreateStaticText(
            Transform parent,
            string text,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            float x,
            float y,
            float width,
            float height)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            SetTopLeftRect(textObject.GetComponent<RectTransform>(), x, y, width, height);

            Text label = textObject.GetComponent<Text>();
            label.raycastTarget = false;
            if (hudFont != null)
            {
                label.font = hudFont;
            }

            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.color = color;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private InputField CreateInputField(Transform parent, float x, float y, float width, float height)
        {
            GameObject inputObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);
            SetTopLeftRect(inputObject.GetComponent<RectTransform>(), x, y, width, height);

            Image background = inputObject.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.98f);

            Text textComponent = CreateInputText(inputObject.transform, "Text", typedUserText, BodyColor, FontStyle.Normal);
            Text placeholder = CreateInputText(inputObject.transform, "Placeholder", "Type objection", new Color(0.32f, 0.42f, 0.43f, 0.72f), FontStyle.Italic);

            InputField field = inputObject.GetComponent<InputField>();
            field.textComponent = textComponent;
            field.placeholder = placeholder;
            field.text = typedUserText;
            field.selectionColor = new Color(0.08f, 0.43f, 0.48f, 0.32f);
            field.onValueChanged.AddListener(value => typedUserText = value);
            field.onEndEdit.AddListener(value =>
            {
                typedUserText = value;
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    SubmitTypedUserText();
                }
            });

            return field;
        }

        private Text CreateInputText(Transform parent, string name, string text, Color color, FontStyle fontStyle)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 4f);
            rect.offsetMax = new Vector2(-8f, -4f);

            Text label = textObject.GetComponent<Text>();
            label.raycastTarget = false;
            if (hudFont != null)
            {
                label.font = hudFont;
            }

            label.text = text;
            label.fontSize = 11;
            label.fontStyle = fontStyle;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private void CreateButton(Transform parent, string label, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            SetTopLeftRect(buttonObject.GetComponent<RectTransform>(), x, y, width, height);

            Image image = buttonObject.GetComponent<Image>();
            image.color = ButtonColor;

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHighlightColor;
            colors.pressedColor = ButtonPressedColor;
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.42f, 0.5f, 0.5f, 0.7f);
            button.colors = colors;
            button.onClick.AddListener(action);

            Text text = CreateStaticText(buttonObject.transform, label, 10, FontStyle.Bold, Color.white, 0f, 0f, width, height);
            text.alignment = TextAnchor.MiddleCenter;
        }

        private static void SetTopLeftRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private Image CreateBlock(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            GameObject blockObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            blockObject.transform.SetParent(parent, false);
            SetTopLeftRect(blockObject.GetComponent<RectTransform>(), x, y, width, height);

            Image image = blockObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Font LoadBuiltinFont()
        {
            Font font = null;

            try
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.ArgumentException)
            {
            }

            if (font != null)
            {
                return font;
            }

            try
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch (System.ArgumentException)
            {
            }

            return font;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystemObject.transform.position = Vector3.zero;
        }

        private void SubmitTypedUserText()
        {
            typedUserText = inputField != null ? inputField.text : typedUserText;
            conversationManager?.SubmitUserText(typedUserText);
            UpdateHudText();
        }

        private void SubmitPreset(string text)
        {
            typedUserText = text;
            if (inputField != null)
            {
                inputField.text = text;
            }

            conversationManager?.SubmitUserText(text);
            UpdateHudText();
        }

        private void OnGUI()
        {
            if (!showHud || !showLegacyOnGui || conversationManager == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(18, 18, LegacyWidth, LegacyHeight), GUI.skin.box);
            GUILayout.Label("CloserXR Sales Negotiator");
            GUILayout.Label("Status: " + conversationManager.Status);
            GUILayout.Label("Gemini: " + (conversationManager.HasGeminiKey ? "connected" : "local fallback"));
            GUILayout.Label("Mic: " + (speechInput != null && speechInput.IsRecording ? "recording" : "hold Space / Quest trigger"));
            GUILayout.Label("Quest: A policy, B premium, X family, Y forward, right stick more lines");

            GUILayout.Space(8);
            GUILayout.Label("User: " + conversationManager.LastUserText);
            GUILayout.Label("Agent: " + conversationManager.LastAgentText);

            GUILayout.Space(8);
            GUI.SetNextControlName("CloserXRTextInput");
            typedUserText = GUILayout.TextField(typedUserText, GUILayout.Width(LegacyWidth - 20));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Send"))
            {
                conversationManager.SubmitUserText(typedUserText);
            }
            GUILayout.EndHorizontal();

            foreach (string quickUserLine in quickUserLines)
            {
                if (GUILayout.Button(quickUserLine))
                {
                    SubmitPreset(quickUserLine);
                }
            }

            GUILayout.EndArea();

            Event current = Event.current;
            if (current.isKey && current.type == EventType.KeyDown && current.keyCode == KeyCode.Return)
            {
                conversationManager.SubmitUserText(typedUserText);
            }
        }

        private static string BlankFallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}
