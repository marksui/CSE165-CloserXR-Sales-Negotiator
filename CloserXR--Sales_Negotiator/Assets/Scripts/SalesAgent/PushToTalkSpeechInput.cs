using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class PushToTalkSpeechInput : MonoBehaviour
    {
        [SerializeField] private SalesConversationManager conversationManager;
        [SerializeField] private KeyCode editorRecordKey = KeyCode.Space;
        [SerializeField] private bool useQuestTrigger = true;
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private int maxRecordSeconds = 8;

        private AudioClip recording;
        private string deviceName;

        public bool IsRecording { get; private set; }

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
            bool questStartPressed = useQuestTrigger &&
                                     QuestRuntimeBridge.GetPrimaryIndexTriggerDown();

            bool questStopPressed = useQuestTrigger &&
                                    QuestRuntimeBridge.GetPrimaryIndexTriggerUp();

            bool startPressed = Input.GetKeyDown(editorRecordKey) || questStartPressed;
            bool stopPressed = Input.GetKeyUp(editorRecordKey) || questStopPressed;

            if (startPressed)
            {
                StartRecording();
            }

            if (stopPressed)
            {
                StopRecording();
            }
        }

        private void StartRecording()
        {
            if (IsRecording || Microphone.devices.Length == 0)
            {
                return;
            }

            deviceName = Microphone.devices[0];
            recording = Microphone.Start(deviceName, false, maxRecordSeconds, sampleRate);
            IsRecording = true;
        }

        private void StopRecording()
        {
            if (!IsRecording)
            {
                return;
            }

            int position = Microphone.GetPosition(deviceName);
            Microphone.End(deviceName);
            IsRecording = false;

            if (recording == null || position <= 0)
            {
                return;
            }

            AudioClip trimmed = TrimClip(recording, position);
            conversationManager?.SubmitUserAudio(trimmed);
            recording = null;
        }

        private static AudioClip TrimClip(AudioClip source, int lengthSamples)
        {
            int channels = source.channels;
            float[] data = new float[lengthSamples * channels];
            source.GetData(data, 0);

            AudioClip clip = AudioClip.Create("CloserXR_UserSpeech", lengthSamples, channels, source.frequency, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
