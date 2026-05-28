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
        [SerializeField] private bool useProject3HeadTrackedView = true;
        [SerializeField] private bool enableVrStatusPanel;
        [SerializeField] private bool enableRoomCameraControls = true;
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
            if (useProject3HeadTrackedView)
            {
                mainCamera = QuestRuntimeBridge.EnsureProject3HeadTrackedView(mainCamera) ?? mainCamera;
            }

            if (repositionDefaultCameraForDemo && mainCamera != null && mainCamera.transform.position.z < -5f)
            {
                mainCamera.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            }

            if (enableRoomCameraControls)
            {
                EnsureRoomCameraControls(mainCamera);
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
            SalesAgentMaterialStyler materialStyler = GetOrAdd<SalesAgentMaterialStyler>();
            SalesAgentFaceFeatures faceFeatures = GetOrAdd<SalesAgentFaceFeatures>();

            pacer.Assign(animator, mainCamera != null ? mainCamera.transform : null);
            pacer.AssignRoomMap(roomMap);
            conversation.Configure(gemini, router, animator, pacer);
            speechInput.Assign(conversation);
            questInput.Assign(conversation);
            hud.Assign(conversation, speechInput);
            vrStatusPanel?.Assign(conversation, speechInput, gemini, roomMap, mainCamera != null ? mainCamera.transform : null);
            materialStyler.ApplyNow();
            faceFeatures.AssignGazeTarget(mainCamera != null ? mainCamera.transform : null);
            faceFeatures.EnsureFace();

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
            QuestRuntimeBridge.EnsurePassthrough(target);
        }

        private static void EnsureRoomCameraControls(Camera mainCamera)
        {
            if (mainCamera == null || Application.platform == RuntimePlatform.Android)
            {
                return;
            }

            if (!mainCamera.TryGetComponent(out RoomCameraController _))
            {
                mainCamera.gameObject.AddComponent<RoomCameraController>();
            }
        }

        private void EnsureSpatialAnchor()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            QuestRuntimeBridge.EnsureSpatialAnchor(gameObject);
        }
    }
}
