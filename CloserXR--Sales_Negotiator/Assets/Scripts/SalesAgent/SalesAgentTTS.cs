using System;
using System.Collections;
using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    /// <summary>
    /// Drives agent speech via Android TTS on device and a silent timing fallback in the editor.
    /// Also runs a procedural lip-variation coroutine while speaking to give the talking
    /// animation a more organic rhythm without requiring real phoneme data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SalesAgentTTS : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 2f)] private float speechRate = 1.1f;
        [SerializeField, Range(0.5f, 2f)] private float pitch = 1.0f;
        [SerializeField] private float wordsPerSecond = 2.8f;

        private SalesAgentAnimator _animator;
        private Coroutine _speakRoutine;
        private Coroutine _lipRoutine;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _tts;
        private volatile bool _ttsReady;
#endif

        private void Awake()
        {
            _animator = GetComponent<SalesAgentAnimator>();
            InitAndroidTTS();
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _tts?.Call("shutdown"); } catch { }
#endif
        }

        public void Assign(SalesAgentAnimator animator)
        {
            _animator = animator;
        }

        public void Speak(string text, Action onComplete)
        {
            Stop();
            _speakRoutine = StartCoroutine(SpeakRoutine(text, onComplete));
        }

        public void Stop()
        {
            if (_speakRoutine != null)
            {
                StopCoroutine(_speakRoutine);
                _speakRoutine = null;
            }

            StopLipVariation();

#if UNITY_ANDROID && !UNITY_EDITOR
            try { _tts?.Call<int>("stop"); } catch { }
#endif
        }

        public float EstimatedDuration(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 1f;
            }

            int wordCount = text.Trim().Split(' ').Length;
            return Mathf.Clamp(wordCount / wordsPerSecond, 2f, 10f);
        }

        private IEnumerator SpeakRoutine(string text, Action onComplete)
        {
            float duration = EstimatedDuration(text);

#if UNITY_ANDROID && !UNITY_EDITOR
            float waited = 0f;
            while (!_ttsReady && waited < 3f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (_ttsReady && _tts != null)
            {
                try
                {
                    _tts.Call<int>("setSpeechRate", speechRate);
                    _tts.Call<int>("setPitch", pitch);
                    _tts.Call<int>("speak", text, 0 /* QUEUE_FLUSH */, null);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SalesAgentTTS] speak() failed: {e.Message}");
                }
            }
#endif

            _lipRoutine = StartCoroutine(VariateTalkingSpeed(duration));
            yield return new WaitForSeconds(duration);

            _speakRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator VariateTalkingSpeed(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                _animator?.SetTalkingSpeed(UnityEngine.Random.Range(0.85f, 1.15f));
                float wait = UnityEngine.Random.Range(0.25f, 0.55f);
                yield return new WaitForSeconds(wait);
                elapsed += wait;
            }

            _animator?.SetTalkingSpeed(1f);
            _lipRoutine = null;
        }

        private void StopLipVariation()
        {
            if (_lipRoutine != null)
            {
                StopCoroutine(_lipRoutine);
                _lipRoutine = null;
            }

            _animator?.SetTalkingSpeed(1f);
        }

        private void InitAndroidTTS()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                AndroidJavaClass playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity");
                _tts = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    activity,
                    new TTSInitListener(OnTTSInitialized));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SalesAgentTTS] Failed to initialize Android TTS: {e.Message}");
            }
#endif
        }

        private void OnTTSInitialized(int status)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _ttsReady = (status == 0);
            if (!_ttsReady)
            {
                Debug.LogWarning("[SalesAgentTTS] Android TTS init failed (status != SUCCESS).");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class TTSInitListener : AndroidJavaProxy
        {
            private readonly Action<int> _callback;

            public TTSInitListener(Action<int> callback)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _callback = callback;
            }

            // Called on the Java thread; only set a volatile bool, no Unity API calls.
            public void onInit(int status) => _callback?.Invoke(status);
        }
#endif
    }
}
