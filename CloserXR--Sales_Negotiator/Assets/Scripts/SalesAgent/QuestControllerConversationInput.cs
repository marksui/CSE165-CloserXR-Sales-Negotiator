using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class QuestControllerConversationInput : MonoBehaviour
    {
        [SerializeField] private SalesConversationManager conversationManager;
        [SerializeField] private bool enableQuestButtons = true;
        [SerializeField] private float submitCooldown = 0.25f;

        [Header("Face Buttons")]
        [SerializeField] private string aButtonLine = "What kind of life insurance is this?";
        [SerializeField] private string bButtonLine = "The premium is too expensive";
        [SerializeField] private string xButtonLine = "How does this protect my family?";
        [SerializeField] private string yButtonLine = "I want to move forward";

        [Header("Right Thumbstick")]
        [SerializeField] private string thumbstickUpLine = "How much coverage do I need?";
        [SerializeField] private string thumbstickDownLine = "I'm not interested";
        [SerializeField] private string thumbstickLeftLine = "Is this term or whole life?";
        [SerializeField] private string thumbstickRightLine = "Maybe I need to think about it";

        private float nextSubmitTime;

        private void Awake()
        {
            if (conversationManager == null)
            {
                conversationManager = GetComponent<SalesConversationManager>();
            }
        }

        public void Assign(SalesConversationManager manager)
        {
            conversationManager = manager;
        }

        private void Update()
        {
            if (!enableQuestButtons || conversationManager == null || Time.time < nextSubmitTime)
            {
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("A"))
            {
                Submit(aButtonLine);
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("B"))
            {
                Submit(bButtonLine);
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("X"))
            {
                Submit(xButtonLine);
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("Y"))
            {
                Submit(yButtonLine);
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("RThumbstickUp"))
            {
                Submit(thumbstickUpLine);
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("RThumbstickDown"))
            {
                Submit(thumbstickDownLine);
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("RThumbstickLeft"))
            {
                Submit(thumbstickLeftLine);
                return;
            }

            if (QuestRuntimeBridge.GetRawButtonDown("RThumbstickRight"))
            {
                Submit(thumbstickRightLine);
            }
        }

        private void Submit(string line)
        {
            nextSubmitTime = Time.time + submitCooldown;
            conversationManager.SubmitUserText(line);
        }
    }
}
