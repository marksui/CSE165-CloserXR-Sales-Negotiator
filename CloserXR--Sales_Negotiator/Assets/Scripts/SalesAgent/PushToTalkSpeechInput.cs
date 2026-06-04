using System;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    public enum SpeechInputMode
    {
        PushToTalk,               // tap trigger / Space to record, tap again to submit
        AutoVAD,                  // Option A: amplitude threshold + silence timeout
        AndroidSpeechRecognizer   // Option B: Android STT (device only, editor falls back to AutoVAD)
    }

    [DisallowMultipleComponent]
    public sealed class PushToTalkSpeechInput : MonoBehaviour
    {
        [Header("Mode")]
        [SerializeField] private SpeechInputMode mode = SpeechInputMode.PushToTalk;

        [Header("Microphone")]
        [SerializeField] private KeyCode editorRecordKey = KeyCode.Space;
        [SerializeField] private bool useQuestTrigger = true;
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private int maxRecordSeconds = 8;

        [Header("Auto VAD (Option A)")]
        [Tooltip("RMS amplitude that triggers speech detection. Lower = more sensitive.")]
        [SerializeField, Range(0.005f, 0.1f)] private float vadThreshold = 0.02f;
        [Tooltip("Seconds of silence after speech before submitting.")]
        [SerializeField, Range(0.5f, 2.5f)] private float silenceSeconds = 1.2f;
        [Tooltip("Seconds of audio captured before the VAD trigger, to avoid clipping consonants.")]
        [SerializeField, Range(0f, 0.5f)] private float preRollSeconds = 0.2f;

        [Header("Muting")]
        [Tooltip("Seconds to keep the mic muted after the agent finishes speaking, to avoid echo pickup.")]
        [SerializeField, Range(0f, 1.5f)] private float postAgentMuteSeconds = 0.5f;

        private SalesConversationManager conversationManager;

        // Shared mic state
        private AudioClip micClip;
        private string deviceName;

        // VAD state
        private bool inSpeech;
        private int speechStartPos;
        private float silenceTimer;
        private bool pttOverride; // trigger toggle override in AutoVAD mode

        // Mute state
        private float muteTimer;
        private bool wasBusy;

        // Android STT state
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _speechRecognizer;
        private bool _sttListening;
        private float _sttRestartCooldown;
#endif

        // ── Public API ──────────────────────────────────────────────────────

        public bool IsRecording { get; private set; }

        // True in auto modes when the mic is open and listening (not yet recording a word).
        public bool IsListening => mode != SpeechInputMode.PushToTalk && micClip != null && !IsMuted && !IsRecording;

        // True while the agent is busy (speaking or waiting for Gemini) plus a short tail.
        public bool IsMuted => (conversationManager != null && conversationManager.IsBusy) || muteTimer > 0f;

        public SpeechInputMode Mode => mode;

        public void Assign(SalesConversationManager manager)
        {
            conversationManager = manager;
        }

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            conversationManager = conversationManager != null
                ? conversationManager
                : GetComponent<SalesConversationManager>();
        }

        private void Start()
        {
            switch (mode)
            {
                case SpeechInputMode.AutoVAD:
                    StartMonitorLoop();
                    break;
                case SpeechInputMode.AndroidSpeechRecognizer:
                    InitAndroidSTT();
                    break;
            }
        }

        private void Update()
        {
            TrackAgentMute();
            HandlePTTInput();

            switch (mode)
            {
                case SpeechInputMode.AutoVAD:
                    UpdateVAD();
                    break;
                case SpeechInputMode.AndroidSpeechRecognizer:
                    UpdateAndroidSTT();
                    break;
            }
        }

        private void OnDestroy()
        {
            StopMicLoop();
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _speechRecognizer?.Call("destroy"); } catch { }
#endif
        }

        // ── Mute tracking ───────────────────────────────────────────────────

        private void TrackAgentMute()
        {
            bool isBusy = conversationManager != null && conversationManager.IsBusy;
            if (wasBusy && !isBusy)
            {
                muteTimer = postAgentMuteSeconds;
            }
            wasBusy = isBusy;

            if (muteTimer > 0f)
            {
                muteTimer -= Time.deltaTime;
            }
        }

        // ── Push-to-talk input (works in all modes) ─────────────────────────

        private void HandlePTTInput()
        {
            bool togglePressed = (useQuestTrigger && QuestRuntimeBridge.GetRightIndexTriggerDown())
                || Input.GetKeyDown(editorRecordKey);

            if (!togglePressed)
            {
                return;
            }

            switch (mode)
            {
                case SpeechInputMode.PushToTalk:
                    if (IsRecording)
                    {
                        StopPTTAndSubmit();
                    }
                    else
                    {
                        StartPTTRecording();
                    }
                    break;

                case SpeechInputMode.AutoVAD:
                    // Trigger acts as a tap-to-start / tap-to-submit override.
                    if (pttOverride || IsRecording || inSpeech)
                    {
                        StopVADOverrideAndSubmit();
                    }
                    else
                    {
                        pttOverride = ForceVADStart();
                    }
                    break;

                // AndroidSTT: push-to-talk override not supported (different pipeline).
            }
        }

        // ── Push-to-talk mode ───────────────────────────────────────────────

        private void StartPTTRecording()
        {
            if (Microphone.devices.Length == 0) return;
            deviceName = Microphone.devices[0];
            micClip = Microphone.Start(deviceName, false, maxRecordSeconds, sampleRate);
            IsRecording = micClip != null;
        }

        private void StopPTTAndSubmit()
        {
            int position = Microphone.GetPosition(deviceName);
            Microphone.End(deviceName);
            IsRecording = false;

            if (micClip == null || position <= 0) return;

            int channels = micClip.channels;
            float[] data = new float[position * channels];
            micClip.GetData(data, 0);

            AudioClip trimmed = AudioClip.Create("UserSpeech", position, channels, sampleRate, false);
            trimmed.SetData(data, 0);
            conversationManager?.SubmitUserAudio(trimmed);
            micClip = null;
        }

        // ── Auto VAD (Option A) ─────────────────────────────────────────────

        private void StartMonitorLoop()
        {
            if (Microphone.devices.Length == 0) return;
            deviceName = Microphone.devices[0];
            micClip = Microphone.Start(deviceName, true, maxRecordSeconds, sampleRate);
        }

        private void StopMicLoop()
        {
            if (micClip != null && mode == SpeechInputMode.AutoVAD)
            {
                Microphone.End(deviceName);
                micClip = null;
            }
        }

        private void UpdateVAD()
        {
            if (micClip == null || pttOverride) return;

            if (IsMuted)
            {
                if (inSpeech) { inSpeech = false; IsRecording = false; }
                return;
            }

            float amplitude = SampleAmplitude();

            if (!inSpeech)
            {
                if (amplitude >= vadThreshold)
                {
                    inSpeech = true;
                    IsRecording = true;
                    silenceTimer = 0f;
                    // Roll back the start position by preRollSeconds so leading consonants aren't clipped.
                    int preRoll = Mathf.RoundToInt(preRollSeconds * sampleRate);
                    int totalSamples = micClip.samples;
                    int pos = Microphone.GetPosition(deviceName);
                    speechStartPos = (pos - preRoll + totalSamples) % totalSamples;
                }
            }
            else
            {
                if (amplitude >= vadThreshold)
                {
                    silenceTimer = 0f;
                }
                else
                {
                    silenceTimer += Time.deltaTime;
                    if (silenceTimer >= silenceSeconds)
                    {
                        ExtractAndSubmit();
                        inSpeech = false;
                        IsRecording = false;
                    }
                }
            }
        }

        private bool ForceVADStart()
        {
            if (micClip == null) return false;
            inSpeech = true;
            IsRecording = true;
            silenceTimer = 0f;
            int preRoll = Mathf.RoundToInt(preRollSeconds * sampleRate);
            int totalSamples = micClip.samples;
            int pos = Microphone.GetPosition(deviceName);
            speechStartPos = (pos - preRoll + totalSamples) % totalSamples;
            return true;
        }

        private void StopVADOverrideAndSubmit()
        {
            pttOverride = false;
            if (inSpeech)
            {
                ExtractAndSubmit();
            }

            inSpeech = false;
            IsRecording = false;
        }

        private float SampleAmplitude()
        {
            if (micClip == null) return 0f;
            int pos = Microphone.GetPosition(deviceName);
            int windowSamples = sampleRate / 10; // 100 ms window
            if (pos < windowSamples) return 0f;

            float[] samples = new float[windowSamples];
            micClip.GetData(samples, pos - windowSamples);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            return Mathf.Sqrt(sum / samples.Length);
        }

        private void ExtractAndSubmit()
        {
            if (micClip == null || conversationManager == null) return;

            int endPos = Microphone.GetPosition(deviceName);
            int totalSamples = micClip.samples;

            int speechSamples = endPos >= speechStartPos
                ? endPos - speechStartPos
                : totalSamples - speechStartPos + endPos;

            speechSamples = Mathf.Clamp(speechSamples, 0, totalSamples);

            // Discard clips shorter than 250 ms — likely noise.
            if (speechSamples < sampleRate / 4) return;

            float[] speech = new float[speechSamples];
            // AudioClip.GetData wraps automatically for looping clips.
            micClip.GetData(speech, speechStartPos);

            AudioClip clip = AudioClip.Create("UserSpeech", speechSamples, 1, sampleRate, false);
            clip.SetData(speech, 0);
            conversationManager.SubmitUserAudio(clip);
        }

        // ── Android SpeechRecognizer (Option B) ─────────────────────────────

        private void InitAndroidSTT()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                AndroidJavaClass playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity");

                AndroidJavaClass recognizerClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
                _speechRecognizer = recognizerClass.CallStatic<AndroidJavaObject>(
                    "createSpeechRecognizer", activity);

                _speechRecognizer.Call("setRecognitionListener", new STTListener(
                    onResult: text =>
                    {
                        IsRecording = false;
                        _sttListening = false;
                        conversationManager?.SubmitUserText(text);
                    },
                    onError: () =>
                    {
                        IsRecording = false;
                        _sttListening = false;
                        _sttRestartCooldown = 0.8f;
                    }));

                StartAndroidListening();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpeechInput] Android SpeechRecognizer init failed ({e.Message}). Falling back to AutoVAD.");
                mode = SpeechInputMode.AutoVAD;
                StartMonitorLoop();
            }
#else
            Debug.Log("[SpeechInput] AndroidSpeechRecognizer is device-only. Running AutoVAD in editor.");
            mode = SpeechInputMode.AutoVAD;
            StartMonitorLoop();
#endif
        }

        private void UpdateAndroidSTT()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_sttRestartCooldown > 0f)
            {
                _sttRestartCooldown -= Time.deltaTime;
                return;
            }

            if (!_sttListening && !IsMuted)
            {
                StartAndroidListening();
            }
            else if (_sttListening && IsMuted)
            {
                // Agent started speaking — stop listening to avoid picking up TTS.
                try { _speechRecognizer?.Call("stopListening"); } catch { }
                _sttListening = false;
                IsRecording = false;
            }
#endif
        }

        private void StartAndroidListening()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                AndroidJavaClass ri = new AndroidJavaClass("android.speech.RecognizerIntent");
                AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent",
                    ri.GetStatic<string>("ACTION_RECOGNIZE_SPEECH"));
                intent.Call<AndroidJavaObject>("putExtra",
                    ri.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                    ri.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));
                intent.Call<AndroidJavaObject>("putExtra",
                    ri.GetStatic<string>("EXTRA_LANGUAGE"), "en-US");

                _speechRecognizer.Call("startListening", intent);
                _sttListening = true;
                IsRecording = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpeechInput] startListening failed: {e.Message}");
                _sttListening = false;
                IsRecording = false;
                _sttRestartCooldown = 0.8f;
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class STTListener : AndroidJavaProxy
        {
            private readonly Action<string> _onResult;
            private readonly Action _onError;

            public STTListener(Action<string> onResult, Action onError)
                : base("android.speech.RecognitionListener")
            {
                _onResult = onResult;
                _onError = onError;
            }

            // All callbacks arrive on the Android main thread (= Unity main thread).
            public void onReadyForSpeech(AndroidJavaObject bundle) { }
            public void onBeginningOfSpeech() { }
            public void onRmsChanged(float rmsdB) { }
            public void onBufferReceived(AndroidJavaObject buffer) { }
            public void onEndOfSpeech() { }
            public void onPartialResults(AndroidJavaObject bundle) { }
            public void onEvent(int eventType, AndroidJavaObject bundle) { }

            public void onError(int error) => _onError?.Invoke();

            public void onResults(AndroidJavaObject results)
            {
                try
                {
                    string key = new AndroidJavaClass("android.speech.SpeechRecognizer")
                        .GetStatic<string>("RESULTS_RECOGNITION");
                    AndroidJavaObject list = results.Call<AndroidJavaObject>("getStringArrayList", key);
                    string text = list?.Call<string>("get", 0);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _onResult?.Invoke(text);
                    }
                    else
                    {
                        _onError?.Invoke();
                    }
                }
                catch
                {
                    _onError?.Invoke();
                }
            }
        }
#endif
    }
}
