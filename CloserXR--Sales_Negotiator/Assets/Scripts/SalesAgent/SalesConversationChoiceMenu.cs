using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesConversationChoiceMenu : MonoBehaviour
    {
        [SerializeField] private SalesConversationManager conversationManager;
        [SerializeField] private SalesAgentAnimator agentAnimator;
        [SerializeField] private SalesAgentPacer pacer;
        [SerializeField] private Transform userHead;
        [SerializeField] private Vector3 headRelativeOffset = new Vector3(-0.58f, -0.16f, 1.18f);
        [SerializeField] private Vector2 menuSize = new Vector2(600f, 510f);
        [SerializeField] private float menuScale = 0.00115f;
        [SerializeField] private float followSharpness = 18f;
        [SerializeField] private float rayLength = 3.5f;
        [SerializeField] private float rayWidth = 0.004f;
        [SerializeField] private float rayMenuOverdraw = 0.08f;
        [SerializeField] private int raySortingOrder = 5000;
        [SerializeField] private Color rayColor = new Color(0.1f, 0.45f, 1f, 0.95f);

        private static int openMenuCount;

        private Canvas canvas;
        private RectTransform panelRect;
        private Text statusText;
        private Text userText;
        private Text agentText;
        private LineRenderer rayLine;
        private Font menuFont;
        private bool built;
        private bool menuOpen;
        private int hoveredChoice = -1;

        private readonly List<ChoiceRow> choices = new List<ChoiceRow>();
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

        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.11f, 0.78f);
        private static readonly Color HeaderColor = new Color(0.16f, 0.32f, 0.56f, 0.86f);
        private static readonly Color RowColor = new Color(0.12f, 0.15f, 0.18f, 0.9f);
        private static readonly Color ActionRowColor = new Color(0.08f, 0.28f, 0.32f, 0.92f);
        private static readonly Color RowHoverColor = new Color(0.08f, 0.36f, 0.72f, 0.96f);
        private static readonly Color RowBorderColor = new Color(0.38f, 0.66f, 1f, 0.44f);
        private static readonly Color TextColor = new Color(0.94f, 0.97f, 1f, 1f);
        private static readonly Color MutedTextColor = new Color(0.72f, 0.8f, 0.9f, 1f);

        public static bool IsAnyMenuOpen => openMenuCount > 0;

        public void Assign(SalesConversationManager manager, Transform head)
        {
            conversationManager = manager;
            userHead = head;
            AutoWireActionTargets();
        }

        private void Awake()
        {
            if (conversationManager == null)
            {
                conversationManager = GetComponent<SalesConversationManager>();
            }

            AutoWireActionTargets();
        }

        private void LateUpdate()
        {
            if (QuestRuntimeBridge.GetLeftMenuToggleDown() || Input.GetKeyDown(KeyCode.M))
            {
                SetMenuOpen(!menuOpen);
            }

            if (!menuOpen)
            {
                SetRayVisible(false);
                return;
            }

            EnsureBuilt();
            UpdatePose();
            UpdateText();
            UpdatePointer();

            if (QuestRuntimeBridge.GetLeftIndexTriggerDown() || Input.GetKeyDown(KeyCode.Return))
            {
                SubmitHoveredChoice();
            }
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                if (canvas != null)
                {
                    canvas.gameObject.SetActive(menuOpen);
                }

                return;
            }

            built = true;
            menuFont = LoadBuiltinFont();

            GameObject canvasObject = new GameObject("CloserXR Left Controller Choice Menu", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 120;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 12f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = menuSize;
            canvasRect.localScale = Vector3.one * menuScale;

            Image panel = CreateBlock(canvasObject.transform, "Panel", Vector2.zero, menuSize, PanelColor);
            panelRect = panel.rectTransform;
            panel.raycastTarget = false;

            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = RowBorderColor;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            CreateBlock(panel.transform, "Header", new Vector2(0f, -14f), new Vector2(menuSize.x - 28f, 76f), HeaderColor);
            Text title = CreateText(panel.transform, "CloserXR Sales Negotiator", 22, FontStyle.Bold, TextColor, TextAnchor.UpperLeft);
            SetRect(title.rectTransform, new Vector2(18f, -22f), new Vector2(menuSize.x - 36f, 28f));

            statusText = CreateText(panel.transform, "", 15, FontStyle.Bold, MutedTextColor, TextAnchor.UpperLeft);
            SetRect(statusText.rectTransform, new Vector2(18f, -50f), new Vector2(menuSize.x - 36f, 22f));

            userText = CreateText(panel.transform, "", 14, FontStyle.Normal, TextColor, TextAnchor.UpperLeft);
            SetRect(userText.rectTransform, new Vector2(18f, -96f), new Vector2(menuSize.x - 36f, 36f));

            agentText = CreateText(panel.transform, "", 14, FontStyle.Normal, MutedTextColor, TextAnchor.UpperLeft);
            SetRect(agentText.rectTransform, new Vector2(18f, -136f), new Vector2(menuSize.x - 36f, 54f));

            float actionY = 206f;
            float actionGap = 8f;
            float actionWidth = (menuSize.x - 36f - actionGap * 2f) / 3f;
            CreateChoiceRow(panel.transform, choices.Count, "Idle", new Vector2(18f, -actionY), new Vector2(actionWidth, 30f), PlayIdleAction, ActionRowColor);
            CreateChoiceRow(panel.transform, choices.Count, "Walk", new Vector2(18f + actionWidth + actionGap, -actionY), new Vector2(actionWidth, 30f), PlayWalkAction, ActionRowColor);
            CreateChoiceRow(panel.transform, choices.Count, "Dance", new Vector2(18f + (actionWidth + actionGap) * 2f, -actionY), new Vector2(actionWidth, 30f), PlayDanceAction, ActionRowColor);

            float y = 248f;
            for (int i = 0; i < quickUserLines.Length; i++)
            {
                CreateChoiceRow(panel.transform, choices.Count, quickUserLines[i], new Vector2(18f, -y), new Vector2(menuSize.x - 36f, 27f), null, RowColor);
                y += 31f;
            }

            canvasObject.SetActive(menuOpen);
        }

        private void CreateChoiceRow(Transform parent, int index, string line, Vector2 anchoredPosition, Vector2 size, Action action, Color normalColor)
        {
            Image rowImage = CreateBlock(parent, "Choice " + index, anchoredPosition, size, normalColor);
            Outline outline = rowImage.gameObject.AddComponent<Outline>();
            outline.effectColor = RowBorderColor;
            outline.effectDistance = new Vector2(0.75f, -0.75f);

            Text label = CreateText(rowImage.transform, line, 14, FontStyle.Bold, TextColor, TextAnchor.MiddleCenter);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 2f);
            labelRect.offsetMax = new Vector2(-8f, -2f);

            choices.Add(new ChoiceRow
            {
                Rect = rowImage.rectTransform,
                Image = rowImage,
                Line = line,
                Action = action,
                NormalColor = normalColor
            });
        }

        private void UpdatePose()
        {
            if (userHead == null && Camera.main != null)
            {
                userHead = Camera.main.transform;
            }

            if (userHead == null || canvas == null)
            {
                return;
            }

            Transform menuTransform = canvas.transform;
            Vector3 targetPosition = userHead.TransformPoint(headRelativeOffset);
            Quaternion targetRotation = Quaternion.LookRotation(targetPosition - userHead.position, userHead.up);
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);

            menuTransform.position = Vector3.Lerp(menuTransform.position, targetPosition, t);
            menuTransform.rotation = Quaternion.Slerp(menuTransform.rotation, targetRotation, t);
        }

        private void UpdateText()
        {
            if (conversationManager == null)
            {
                return;
            }

            if (statusText != null)
            {
                statusText.text = "Status: " + conversationManager.Status;
            }

            if (userText != null)
            {
                userText.text = "User: " + Compact(conversationManager.LastUserText, 88);
            }

            if (agentText != null)
            {
                agentText.text = "Agent: " + Compact(conversationManager.LastAgentText, 118);
            }
        }

        private void UpdatePointer()
        {
            Ray ray;
            QuestRuntimeBridge.TryGetLeftControllerRay(out ray);

            float endDistance = rayLength;
            int nextHoveredChoice = -1;

            if (canvas != null)
            {
                Plane menuPlane = new Plane(canvas.transform.forward, canvas.transform.position);
                if (menuPlane.Raycast(ray, out float hitDistance) && hitDistance > 0f && hitDistance <= rayLength)
                {
                    Vector3 hitPoint = ray.GetPoint(hitDistance);
                    endDistance = Mathf.Min(rayLength, hitDistance + rayMenuOverdraw);
                    nextHoveredChoice = FindChoiceAt(hitPoint);
                }
            }

            SetHoveredChoice(nextHoveredChoice);
            SetRay(ray.origin, ray.GetPoint(endDistance));
        }

        private int FindChoiceAt(Vector3 worldPoint)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                RectTransform rect = choices[i].Rect;
                Vector3 localPoint = rect.InverseTransformPoint(worldPoint);
                if (rect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                {
                    return i;
                }
            }

            return -1;
        }

        private void SubmitHoveredChoice()
        {
            if (hoveredChoice < 0 || hoveredChoice >= choices.Count)
            {
                return;
            }

            ChoiceRow choice = choices[hoveredChoice];
            if (choice.Action != null)
            {
                choice.Action.Invoke();
            }
            else
            {
                conversationManager?.SubmitUserText(choice.Line);
            }

            SetMenuOpen(false);
        }

        private void SetHoveredChoice(int index)
        {
            if (hoveredChoice == index)
            {
                return;
            }

            hoveredChoice = index;
            for (int i = 0; i < choices.Count; i++)
            {
                choices[i].Image.color = i == hoveredChoice ? RowHoverColor : choices[i].NormalColor;
            }
        }

        private void SetMenuOpen(bool open)
        {
            if (menuOpen == open)
            {
                return;
            }

            menuOpen = open;
            openMenuCount = Mathf.Max(0, openMenuCount + (open ? 1 : -1));
            EnsureBuilt();

            if (canvas != null)
            {
                canvas.gameObject.SetActive(menuOpen);
            }

            if (menuOpen)
            {
                UpdatePose();
                UpdateText();
            }
            else
            {
                SetHoveredChoice(-1);
                SetRayVisible(false);
            }
        }

        private void SetRay(Vector3 start, Vector3 end)
        {
            EnsureRay();
            rayLine.gameObject.SetActive(true);
            rayLine.SetPosition(0, start);
            rayLine.SetPosition(1, end);
        }

        private void SetRayVisible(bool visible)
        {
            if (rayLine != null)
            {
                rayLine.gameObject.SetActive(visible);
            }
        }

        private void EnsureRay()
        {
            if (rayLine != null)
            {
                return;
            }

            GameObject rayObject = new GameObject("CloserXR Left Menu Ray");
            rayLine = rayObject.AddComponent<LineRenderer>();
            rayLine.useWorldSpace = true;
            rayLine.positionCount = 2;
            rayLine.widthMultiplier = rayWidth;
            rayLine.numCapVertices = 6;
            rayLine.startColor = rayColor;
            rayLine.endColor = rayColor;
            rayLine.sortingOrder = raySortingOrder;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader != null)
            {
                Material rayMaterial = new Material(shader);
                rayMaterial.renderQueue = (int)RenderQueue.Overlay;

                if (rayMaterial.HasProperty("_ZTest"))
                {
                    rayMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                }

                if (rayMaterial.HasProperty("_ZWrite"))
                {
                    rayMaterial.SetInt("_ZWrite", 0);
                }

                if (rayMaterial.HasProperty("_SrcBlend"))
                {
                    rayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                }

                if (rayMaterial.HasProperty("_DstBlend"))
                {
                    rayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                }

                rayLine.material = rayMaterial;
            }

            rayObject.SetActive(false);
        }

        private void PlayIdleAction()
        {
            AutoWireActionTargets();
            pacer?.GoIdle();
            agentAnimator?.ResetToIdle();
        }

        private void PlayWalkAction()
        {
            AutoWireActionTargets();
            agentAnimator?.ResetToIdle();
            pacer?.StartManualPacing();
        }

        private void PlayDanceAction()
        {
            AutoWireActionTargets();
            pacer?.GoIdle();
            agentAnimator?.Dance();
        }

        private void AutoWireActionTargets()
        {
            if (agentAnimator == null)
            {
                agentAnimator = GetComponent<SalesAgentAnimator>();
            }

            if (pacer == null)
            {
                pacer = GetComponent<SalesAgentPacer>();
            }
        }

        private Image CreateBlock(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject blockObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            blockObject.transform.SetParent(parent, false);
            Image image = blockObject.GetComponent<Image>();
            image.color = color;
            SetRect(image.rectTransform, anchoredPosition, size);
            return image;
        }

        private Text CreateText(Transform parent, string text, int fontSize, FontStyle style, Color color, TextAnchor alignment)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text label = textObject.GetComponent<Text>();
            if (menuFont != null)
            {
                label.font = menuFont;
            }

            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        private static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static string Compact(string value, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            value = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return value.Length <= maxCharacters ? value : value.Substring(0, maxCharacters - 3) + "...";
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

        private void OnDestroy()
        {
            if (menuOpen)
            {
                openMenuCount = Mathf.Max(0, openMenuCount - 1);
            }

            if (canvas != null)
            {
                Destroy(canvas.gameObject);
            }

            if (rayLine != null)
            {
                Destroy(rayLine.gameObject);
            }
        }

        private sealed class ChoiceRow
        {
            public RectTransform Rect;
            public Image Image;
            public string Line;
            public Action Action;
            public Color NormalColor;
        }
    }
}
