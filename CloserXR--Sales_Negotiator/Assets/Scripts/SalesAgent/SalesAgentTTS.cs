using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

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
        [Header("Voice Audio")]
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1.0f;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 0.75f;
        [SerializeField] private float minDistance = 0.75f;
        [SerializeField] private float maxDistance = 14f;
        [SerializeField] private bool proceduralFallbackVoice = true;
        [SerializeField] private bool allowAndroidSystemTtsFallback = true;

        private SalesAgentAnimator _animator;
        private AudioSource _audioSource;
        private Coroutine _speakRoutine;
        private Coroutine _lipRoutine;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _tts;
        private TTSUtteranceProgressListener _progressListener;
        private volatile bool _ttsReady;
        private volatile bool _ttsFinished;
        private volatile string _ttsError;
        private string _currentUtteranceId;
        private string _currentSynthPath;
#endif

        public string DiagnosticStatus { get; private set; } = "Not initialized";

        private void Awake()
        {
            _animator = GetComponent<SalesAgentAnimator>();
            EnsureAudioSource();
#if UNITY_ANDROID && !UNITY_EDITOR
            DiagnosticStatus = "Initializing...";
#else
            _ = speechRate;
            _ = pitch;
            _ = allowAndroidSystemTtsFallback;
            DiagnosticStatus = "Editor simulated (no audio)";
#endif
            InitAndroidTTS();
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _tts?.Call("shutdown"); } catch { }
            TryDeleteFile(_currentSynthPath);
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
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.clip = null;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try { _tts?.Call<int>("stop"); } catch { }
            _ttsFinished = true;
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
            float lipDuration = Mathf.Clamp(duration + 2f, 3f, 14f);
            bool handledSpeech = false;

            _lipRoutine = StartCoroutine(VariateTalkingSpeed(lipDuration));

#if UNITY_ANDROID && !UNITY_EDITOR
            float waited = 0f;
            while (!_ttsReady && waited < 3f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            string androidError = null;
            if (_ttsReady && _tts != null)
            {
                yield return PlayAndroidTextToSpeech(text, duration, result => handledSpeech = result, error => androidError = error);
            }

            if (!handledSpeech)
            {
                DiagnosticStatus = string.IsNullOrEmpty(androidError)
                    ? "TTS init timeout; fallback audio"
                    : androidError + "; fallback audio";
            }
#else
            DiagnosticStatus = "Editor fallback audio";
#endif

            if (!handledSpeech)
            {
                if (proceduralFallbackVoice)
                {
                    yield return PlayProceduralFallbackTone(text, duration);
                }
                else
                {
                    yield return new WaitForSeconds(duration);
                }
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            else if (!string.IsNullOrEmpty(androidError))
            {
                DiagnosticStatus = androidError;
            }
#endif

            StopLipVariation();
            DiagnosticStatus = "Ready";
            _speakRoutine = null;
            onComplete?.Invoke();
        }

        private void EnsureAudioSource()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.volume = voiceVolume;
            _audioSource.spatialBlend = spatialBlend;
            _audioSource.minDistance = minDistance;
            _audioSource.maxDistance = maxDistance;
            _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
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
                AndroidJavaObject appContext = activity.Call<AndroidJavaObject>("getApplicationContext");
                _tts = new AndroidJavaObject(
                    "android.speech.tts.TextToSpeech",
                    appContext,
                    new TTSInitListener(OnTTSInitialized));
            }
            catch (Exception e)
            {
                DiagnosticStatus = $"TTS init exception: {e.Message}";
                Debug.LogWarning($"[SalesAgentTTS] Failed to initialize Android TTS: {e.Message}");
            }
#endif
        }

        private void OnTTSInitialized(int status)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _ttsReady = status == 0;
            DiagnosticStatus = _ttsReady ? "Ready" : $"Init failed (status {status})";
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator PlayAndroidTextToSpeech(
            string text,
            float estimatedDuration,
            Action<bool> onComplete,
            Action<string> onError)
        {
            onComplete?.Invoke(false);
            onError?.Invoke(null);

            string error = null;
            if (TryStartAndroidSynthesis(text, out string synthPath, out error))
            {
                DiagnosticStatus = "Synthesizing voice";
                yield return WaitForAndroidUtterance(Mathf.Clamp(estimatedDuration + 6f, 6f, 18f));

                if (string.IsNullOrEmpty(_ttsError) && _ttsFinished && File.Exists(synthPath))
                {
                    bool played = false;
                    yield return PlaySynthesizedFile(synthPath, result => played = result, resultError => error = resultError);
                    TryDeleteFile(synthPath);

                    if (played)
                    {
                        onComplete?.Invoke(true);
                        yield break;
                    }
                }
                else
                {
                    error = !string.IsNullOrEmpty(_ttsError)
                        ? _ttsError
                        : "TTS file synthesis timeout";
                    TryDeleteFile(synthPath);
                }
            }

            if (allowAndroidSystemTtsFallback && TryStartAndroidSpeech(text, out error))
            {
                DiagnosticStatus = "Speaking English";
                yield return WaitForAndroidUtterance(Mathf.Clamp(estimatedDuration + 4f, 4f, 14f));

                if (string.IsNullOrEmpty(_ttsError) && _ttsFinished)
                {
                    onComplete?.Invoke(true);
                    yield break;
                }

                error = !string.IsNullOrEmpty(_ttsError) ? _ttsError : "Android TTS timeout";
            }

            onError?.Invoke(error);
        }

        private bool TryStartAndroidSynthesis(string text, out string synthPath, out string error)
        {
            synthPath = null;
            error = null;

            if (!ConfigureAndroidTTSVoice(out error))
            {
                return false;
            }

            try
            {
                _ttsFinished = false;
                _ttsError = null;
                _currentUtteranceId = "closerxr_file_" + DateTime.UtcNow.Ticks;
                _currentSynthPath = Path.Combine(Application.temporaryCachePath, _currentUtteranceId + ".wav");
                synthPath = _currentSynthPath;

                EnsureProgressListener();
                AndroidJavaObject bundle = new AndroidJavaObject("android.os.Bundle");
                AndroidJavaObject file = new AndroidJavaObject("java.io.File", synthPath);
                int result = _tts.Call<int>("synthesizeToFile", text, bundle, file, _currentUtteranceId);
                bundle.Dispose();
                file.Dispose();

                if (result < 0)
                {
                    error = $"TTS file synthesis failed ({result})";
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                error = $"TTS synthesis error: {e.Message}";
                return false;
            }
        }

        private IEnumerator WaitForAndroidUtterance(float timeout)
        {
            float elapsed = 0f;
            while (!_ttsFinished && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator PlaySynthesizedFile(string path, Action<bool> onComplete, Action<string> onError)
        {
            EnsureAudioSource();
            string url = "file://" + path.Replace("\\", "/");
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("TTS audio load failed: " + request.error);
                    onComplete?.Invoke(false);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    onError?.Invoke("TTS audio clip empty");
                    onComplete?.Invoke(false);
                    yield break;
                }

                DiagnosticStatus = "Playing voice";
                _audioSource.clip = clip;
                _audioSource.Play();

                float timeout = Mathf.Max(clip.length + 0.5f, 1f);
                float elapsed = 0f;
                while (_audioSource != null && _audioSource.isPlaying && elapsed < timeout)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                _audioSource.clip = null;
                onComplete?.Invoke(true);
            }
        }

        private bool TryStartAndroidSpeech(string text, out string error)
        {
            error = null;

            if (!ConfigureAndroidTTSVoice(out error))
            {
                return false;
            }

            try
            {
                _ttsFinished = false;
                _ttsError = null;
                _currentUtteranceId = "closerxr_" + DateTime.UtcNow.Ticks;

                EnsureProgressListener();

                AndroidJavaObject bundle = new AndroidJavaObject("android.os.Bundle");
                int speakResult = _tts.Call<int>("speak", text, 0, bundle, _currentUtteranceId);
                bundle.Dispose();

                if (speakResult < 0)
                {
                    error = $"TTS speak failed ({speakResult})";
                    return false;
                }

                DiagnosticStatus = "Speaking";
                return true;
            }
            catch (Exception e)
            {
                error = $"TTS error: {e.Message}";
                return false;
            }
        }

        private bool ConfigureAndroidTTSVoice(out string error)
        {
            error = null;

            try
            {
                ConfigureAndroidAudioAttributes();

                AndroidJavaObject localeUS = new AndroidJavaClass("java.util.Locale")
                    .GetStatic<AndroidJavaObject>("US");
                int langResult = _tts.Call<int>("setLanguage", localeUS);
                if (langResult < 0)
                {
                    error = LanguageError(langResult);
                    return false;
                }

                int rateResult = _tts.Call<int>("setSpeechRate", speechRate);
                int pitchResult = _tts.Call<int>("setPitch", pitch);
                if (rateResult < 0 || pitchResult < 0)
                {
                    error = $"TTS voice setting error ({rateResult}/{pitchResult})";
                    return false;
                }
            }
            catch (Exception e)
            {
                error = $"TTS voice config error: {e.Message}";
                return false;
            }

            return true;
        }

        private void EnsureProgressListener()
        {
            if (_progressListener != null)
            {
                return;
            }

            _progressListener = new TTSUtteranceProgressListener(
                OnAndroidTTSDone,
                OnAndroidTTSError);
            _tts.Call<int>("setOnUtteranceProgressListener", _progressListener);
        }

        private void ConfigureAndroidAudioAttributes()
        {
            try
            {
                AndroidJavaObject builder = new AndroidJavaObject("android.media.AudioAttributes$Builder");
                builder.Call<AndroidJavaObject>("setUsage", 1);
                builder.Call<AndroidJavaObject>("setContentType", 1);
                AndroidJavaObject attributes = builder.Call<AndroidJavaObject>("build");
                _tts.Call<int>("setAudioAttributes", attributes);
                attributes.Dispose();
                builder.Dispose();
            }
            catch
            {
                // Some TTS engines ignore audio attributes and can still speak.
            }
        }

        private static string LanguageError(int langResult)
        {
            switch (langResult)
            {
                case -2:
                    return "No English TTS voice data";
                case -1:
                    return "TTS language not supported";
                default:
                    return $"TTS language error ({langResult})";
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void OnAndroidTTSDone(string utteranceId)
        {
            if (string.Equals(utteranceId, _currentUtteranceId, StringComparison.Ordinal))
            {
                _ttsFinished = true;
            }
        }

        private void OnAndroidTTSError(string utteranceId, string error)
        {
            if (string.Equals(utteranceId, _currentUtteranceId, StringComparison.Ordinal))
            {
                _ttsError = error;
                _ttsFinished = true;
            }
        }

        private sealed class TTSInitListener : AndroidJavaProxy
        {
            private readonly Action<int> _callback;

            public TTSInitListener(Action<int> callback)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _callback = callback;
            }

            public void onInit(int status) => _callback?.Invoke(status);
        }

        private sealed class TTSUtteranceProgressListener : AndroidJavaProxy
        {
            private readonly Action<string> _onDone;
            private readonly Action<string, string> _onError;

            public TTSUtteranceProgressListener(
                Action<string> onDone,
                Action<string, string> onError)
                : base("android.speech.tts.UtteranceProgressListener")
            {
                _onDone = onDone;
                _onError = onError;
            }

            public void onStart(string utteranceId) { }

            public void onDone(string utteranceId) => _onDone?.Invoke(utteranceId);

            public void onError(string utteranceId) =>
                _onError?.Invoke(utteranceId, "TTS utterance error");

            public void onError(string utteranceId, int errorCode) =>
                _onError?.Invoke(utteranceId, $"TTS utterance error ({errorCode})");
        }
#endif

        private IEnumerator PlayProceduralFallbackTone(string text, float duration)
        {
            EnsureAudioSource();
            AudioClip clip = BuildProceduralFallbackTone(text, duration);
            if (clip == null)
            {
                yield return new WaitForSeconds(duration);
                yield break;
            }

            DiagnosticStatus = "Fallback tone (no English)";
            _audioSource.clip = clip;
            _audioSource.Play();

            float elapsed = 0f;
            float timeout = Mathf.Max(clip.length + 0.2f, 0.5f);
            while (_audioSource != null && _audioSource.isPlaying && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _audioSource.clip = null;
        }

        private AudioClip BuildProceduralFallbackTone(string text, float duration)
        {
            int frequency = 22050;
            int samples = Mathf.Max(1, Mathf.CeilToInt(duration * frequency));
            float[] data = new float[samples];
            int hash = string.IsNullOrEmpty(text) ? 17 : text.GetHashCode();
            float basePitch = 125f + Mathf.Abs(hash % 35);

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)frequency;
                float syllable = Mathf.Sin(t * 18f + (hash & 7));
                float pitchOffset = 18f * Mathf.Sin(t * 7.5f) + 8f * Mathf.Sign(syllable);
                float carrier = Mathf.Sin(2f * Mathf.PI * (basePitch + pitchOffset) * t);
                float buzz = Mathf.Sin(2f * Mathf.PI * (basePitch * 2.01f + pitchOffset) * t) * 0.35f;
                float envelope = Mathf.Clamp01(Mathf.Sin(Mathf.PI * t / Mathf.Max(duration, 0.01f)));
                float wordGate = 0.62f + 0.38f * Mathf.Clamp01(Mathf.Sin(t * 23f) * 0.5f + 0.5f);
                data[i] = (carrier + buzz) * envelope * wordGate * voiceVolume * 0.22f;
            }

            AudioClip clip = AudioClip.Create("CloserXRFallbackTone", samples, 1, frequency, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
