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
        [SerializeField] private string aButtonLine = "What are you selling?";
        [SerializeField] private string bButtonLine = "This is too expensive";
        [SerializeField] private string xButtonLine = "Can you prove it works?";
        [SerializeField] private string yButtonLine = "Yes, deal, sign me up";

        [Header("Right Thumbstick")]
        [SerializeField] private string thumbstickUpLine = "What makes this better than competitors?";
        [SerializeField] private string thumbstickDownLine = "I'm not interested";
        [SerializeField] private string thumbstickLeftLine = "Do I have to sign a contract?";
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
