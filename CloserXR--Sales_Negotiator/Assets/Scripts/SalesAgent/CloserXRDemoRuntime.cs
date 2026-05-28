using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class CloserXRDemoRuntime : MonoBehaviour
    {
        [SerializeField] private bool bootstrapOnAwake = true;
        [SerializeField] private bool enablePassthrough = true;
        [SerializeField] private bool enableSpatialAnchorOnDevice = true;
        [SerializeField] private bool enableRoomOutlineDemo = true;
        [SerializeField] private bool enableVrStatusPanel = true;
        [SerializeField] private bool repositionDefaultCameraForDemo = true;

        private void Awake()
        {
            if (!bootstrapOnAwake)
            {
                return;
            }

            Bootstrap();
        }

        public void Bootstrap()
        {
            Camera mainCamera = Camera.main;
            if (repositionDefaultCameraForDemo && mainCamera != null && mainCamera.transform.position.z < -5f)
            {
                mainCamera.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            }

            SalesAgentAnimator animator = GetComponent<SalesAgentAnimator>();
            SalesDialogueGestureRouter router = GetComponent<SalesDialogueGestureRouter>();

            GeminiSalesClient gemini = GetOrAdd<GeminiSalesClient>();
            SalesAgentPacer pacer = GetOrAdd<SalesAgentPacer>();
            SpatialRoomMapDemo roomMap = enableRoomOutlineDemo ? GetOrAdd<SpatialRoomMapDemo>() : null;
            SalesConversationManager conversation = GetOrAdd<SalesConversationManager>();
            PushToTalkSpeechInput speechInput = GetOrAdd<PushToTalkSpeechInput>();
            QuestControllerConversationInput questInput = GetOrAdd<QuestControllerConversationInput>();
            SalesConversationDebugHud hud = GetOrAdd<SalesConversationDebugHud>();
            SalesAgentVRStatusPanel vrStatusPanel = enableVrStatusPanel ? GetOrAdd<SalesAgentVRStatusPanel>() : null;

            pacer.Assign(animator, mainCamera != null ? mainCamera.transform : null);
            pacer.AssignRoomMap(roomMap);
            conversation.Configure(gemini, router, animator, pacer);
            speechInput.Assign(conversation);
            questInput.Assign(conversation);
            hud.Assign(conversation, speechInput);
            vrStatusPanel?.Assign(conversation, speechInput, gemini, roomMap, mainCamera != null ? mainCamera.transform : null);

            if (enablePassthrough)
            {
                EnsurePassthrough(mainCamera);
            }

            if (enableSpatialAnchorOnDevice)
            {
                EnsureSpatialAnchor();
            }
        }

        private T GetOrAdd<T>() where T : Component
        {
            T component = GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void EnsurePassthrough(Camera mainCamera)
        {
            GameObject target = mainCamera != null ? mainCamera.gameObject : new GameObject("CloserXR Camera Runtime");

            OVRManager manager = OVRManager.instance != null
                ? OVRManager.instance
                : target.GetComponent<OVRManager>();

            if (manager == null)
            {
                manager = target.AddComponent<OVRManager>();
            }

            manager.isInsightPassthroughEnabled = true;
            manager.shouldBoundaryVisibilityBeSuppressed = true;

            OVRPassthroughLayer passthroughLayer = Object.FindObjectOfType<OVRPassthroughLayer>();
            if (passthroughLayer == null)
            {
                passthroughLayer = target.AddComponent<OVRPassthroughLayer>();
            }

            passthroughLayer.hidden = false;
            passthroughLayer.textureOpacity = 1f;
            passthroughLayer.edgeRenderingEnabled = false;
        }

        private void EnsureSpatialAnchor()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            if (GetComponent<OVRSpatialAnchor>() == null)
            {
                gameObject.AddComponent<OVRSpatialAnchor>();
            }
        }
    }
}
