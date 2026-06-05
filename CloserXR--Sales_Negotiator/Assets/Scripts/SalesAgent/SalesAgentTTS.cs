using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CloserXR.SalesNegotiator
{
    /// <summary>
    /// Drives agent speech through Gemini English TTS first, then Android TTS on device.
    /// Also runs a procedural lip-variation coroutine while speaking to give the talking
    /// animation a more organic rhythm without requiring real phoneme data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SalesAgentTTS : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 2f)] private float speechRate = 1.1f;
        [SerializeField, Range(0.5f, 2f)] private float pitch = 1.0f;
        [SerializeField] private float wordsPerSecond = 2.8f;
        [Header("Gemini English TTS")]
        [SerializeField] private bool useGeminiEnglishTts = true;
        [SerializeField] private string geminiTtsModel = "gemini-2.5-flash-preview-tts";
        [SerializeField] private string geminiVoiceName = "Kore";
        [SerializeField] private string geminiTtsPromptPrefix = "Say in clear natural American English as a confident life insurance sales agent: ";
        [Header("Voice Audio")]
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1.0f;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 0f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 40f;
        [SerializeField] private bool proceduralFallbackVoice = false;
        [SerializeField] private bool allowAndroidSystemTtsFallback = true;

        private const string GeminiTtsEndpointTemplate =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";
        private const int DefaultGeminiTtsSampleRate = 24000;

        private SalesAgentAnimator _animator;
        private GeminiSalesClient _geminiClient;
        private AudioSource _audioSource;
        private Coroutine _speakRoutine;
        private Coroutine _lipRoutine;
        private string _lastVoiceRoute;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _tts;
        private AndroidJavaObject _mediaPlayer;
        private AndroidJavaObject _toneGenerator;
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
            _geminiClient = GetComponent<GeminiSalesClient>();
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
            ReleaseAndroidMediaPlayer();
            ReleaseAndroidToneGenerator();
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

        public void PlayAudioProbe()
        {
            Stop();
            _speakRoutine = StartCoroutine(AudioProbeRoutine());
        }

        public void Stop()
        {
            if (_speakRoutine != null)
            {
                StopCoroutine(_speakRoutine);
                _speakRoutine = null;
            }

            StopLipVariation();
            ReleaseAndroidMediaPlayer();
            ReleaseAndroidToneGenerator();
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
            string ttsError = null;
            _lastVoiceRoute = null;

            DiagnosticStatus = "Starting voice";
            _lipRoutine = StartCoroutine(VariateTalkingSpeed(lipDuration));

            if (useGeminiEnglishTts)
            {
                yield return PlayGeminiEnglishTextToSpeech(
                    text,
                    result => handledSpeech = result,
                    error => ttsError = error);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!handledSpeech)
            {
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
                    ttsError = string.IsNullOrEmpty(androidError)
                        ? "TTS init timeout"
                        : androidError;
                }
                else
                {
                    ttsError = null;
                }
            }
#else
            if (!handledSpeech && string.IsNullOrEmpty(ttsError))
            {
                ttsError = "No English TTS available";
            }
#endif

            if (!handledSpeech)
            {
                DiagnosticStatus = BuildNoSoundStatus(ttsError);
                Debug.LogWarning($"[SalesAgentTTS] {DiagnosticStatus}");

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
            else if (!string.IsNullOrEmpty(ttsError))
            {
                DiagnosticStatus = ttsError;
            }
#endif

            StopLipVariation();
            if (handledSpeech)
            {
                DiagnosticStatus = string.IsNullOrWhiteSpace(_lastVoiceRoute)
                    ? "Ready (voice ok)"
                    : "Ready (" + _lastVoiceRoute + ")";
            }

            _speakRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator AudioProbeRoutine()
        {
            _lastVoiceRoute = null;
            StopLipVariation();

#if UNITY_ANDROID && !UNITY_EDITOR
            DiagnosticStatus = "Audio probe: Android beep";
            if (TryStartAndroidToneProbe(850, out string toneError))
            {
                yield return new WaitForSeconds(1f);
                ReleaseAndroidToneGenerator();
            }
            else
            {
                Debug.LogWarning("[SalesAgentTTS] Android tone probe failed: " + toneError);
                DiagnosticStatus = "Audio probe: Android beep failed";
                yield return new WaitForSeconds(0.5f);
            }
#endif

            DiagnosticStatus = "Audio probe: Unity beep";
            yield return PlayUnityProbeTone();

            bool handledSpeech = false;
            string ttsError = null;
            DiagnosticStatus = "Audio probe: English TTS";
            yield return PlayGeminiEnglishTextToSpeech(
                "Say clearly in English: Audio test complete.",
                result => handledSpeech = result,
                error => ttsError = error);

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!handledSpeech && _ttsReady && _tts != null)
            {
                yield return PlayAndroidTextToSpeech(
                    "Audio test complete.",
                    2f,
                    result => handledSpeech = result,
                    error => ttsError = error);
            }
#endif

            DiagnosticStatus = handledSpeech
                ? "Audio probe OK (" + (string.IsNullOrWhiteSpace(_lastVoiceRoute) ? "voice ok" : _lastVoiceRoute) + ")"
                : BuildNoSoundStatus(ttsError);
            _speakRoutine = null;
        }

        private void EnsureAudioSource()
        {
            EnsureAudioListener();
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.mute = false;
            _audioSource.volume = voiceVolume;
            _audioSource.spatialBlend = spatialBlend;
            _audioSource.minDistance = minDistance;
            _audioSource.maxDistance = maxDistance;
            _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            _audioSource.priority = 0;
            _audioSource.dopplerLevel = 0f;
            _audioSource.ignoreListenerPause = true;
        }

        private static void EnsureAudioListener()
        {
            AudioListener.volume = 1f;

            foreach (AudioListener listener in FindObjectsOfType<AudioListener>())
            {
                if (listener != null && listener.enabled && listener.gameObject.activeInHierarchy)
                {
                    return;
                }
            }

            Camera camera = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
            if (camera == null)
            {
                Debug.LogWarning("[SalesAgentTTS] No camera found for AudioListener.");
                return;
            }

            AudioListener cameraListener = camera.GetComponent<AudioListener>();
            if (cameraListener == null)
            {
                cameraListener = camera.gameObject.AddComponent<AudioListener>();
            }

            cameraListener.enabled = true;
            Debug.LogWarning("[SalesAgentTTS] Enabled fallback AudioListener on " + camera.name);
        }

        private IEnumerator PlayUnityProbeTone()
        {
            EnsureAudioSource();
            AudioClip clip = BuildProbeTone(880f, 0.6f);
            _audioSource.clip = clip;
            _audioSource.Play();

            float elapsed = 0f;
            while (_audioSource != null && _audioSource.isPlaying && elapsed < 1f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _audioSource.clip = null;
        }

        private AudioClip BuildProbeTone(float pitchHz, float duration)
        {
            int frequency = 24000;
            int samples = Mathf.Max(1, Mathf.CeilToInt(duration * frequency));
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)frequency;
                float envelope = Mathf.Clamp01(Mathf.Sin(Mathf.PI * t / Mathf.Max(duration, 0.01f)));
                data[i] = Mathf.Sin(2f * Mathf.PI * pitchHz * t) * envelope * 0.55f;
            }

            AudioClip clip = AudioClip.Create("CloserXRAudioProbe", samples, 1, frequency, false);
            clip.SetData(data, 0);
            return clip;
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

        private IEnumerator PlayGeminiEnglishTextToSpeech(
            string text,
            Action<bool> onComplete,
            Action<string> onError)
        {
            onComplete?.Invoke(false);
            onError?.Invoke(null);

            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("No speech text");
                yield break;
            }

            _geminiClient = _geminiClient != null ? _geminiClient : GetComponent<GeminiSalesClient>();
            if (_geminiClient == null)
            {
                onError?.Invoke("Gemini TTS missing GeminiSalesClient");
                yield break;
            }

            DiagnosticStatus = "Preparing English voice";
            yield return _geminiClient.EnsureRuntimeApiKeyLoaded();

            string apiKey = _geminiClient.GetRuntimeApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onError?.Invoke("Missing Gemini key for English TTS");
                yield break;
            }

            string modelName = string.IsNullOrWhiteSpace(geminiTtsModel)
                ? "gemini-2.5-flash-preview-tts"
                : geminiTtsModel.Trim();

            GeminiTtsRequest requestBody = new GeminiTtsRequest
            {
                contents = new[]
                {
                    new GeminiTtsContent
                    {
                        parts = new[]
                        {
                            new GeminiTtsPart
                            {
                                text = BuildGeminiSpeechPrompt(text)
                            }
                        }
                    }
                },
                generationConfig = new GeminiTtsGenerationConfig
                {
                    responseModalities = new[] { "AUDIO" },
                    speechConfig = new GeminiTtsSpeechConfig
                    {
                        voiceConfig = new GeminiTtsVoiceConfig
                        {
                            prebuiltVoiceConfig = new GeminiTtsPrebuiltVoiceConfig
                            {
                                voiceName = string.IsNullOrWhiteSpace(geminiVoiceName)
                                    ? "Kore"
                                    : geminiVoiceName.Trim()
                            }
                        }
                    }
                }
            };

            string json = JsonUtility.ToJson(requestBody);
            byte[] body = Encoding.UTF8.GetBytes(json);
            string endpoint = string.Format(GeminiTtsEndpointTemplate, modelName);

            DiagnosticStatus = "Generating English voice";
            using (UnityWebRequest webRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(body);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("x-goog-api-key", apiKey);

                yield return webRequest.SendWebRequest();

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    string message = string.IsNullOrWhiteSpace(webRequest.downloadHandler.text)
                        ? webRequest.error
                        : webRequest.downloadHandler.text;
                    Debug.LogWarning("[SalesAgentTTS] Gemini English TTS request failed: " + message);
                    onError?.Invoke("Gemini English TTS failed: " + message);
                    yield break;
                }

                byte[] pcmBytes = ExtractGeminiPcmBytes(webRequest.downloadHandler.text, out string mimeType);
                if (pcmBytes == null || pcmBytes.Length == 0)
                {
                    onError?.Invoke("Gemini English TTS returned no audio");
                    yield break;
                }

#if UNITY_ANDROID && !UNITY_EDITOR
                bool nativePlayed = false;
                string nativeError = null;
                yield return PlayGeminiPcmWithAndroidMediaPlayer(
                    pcmBytes,
                    mimeType,
                    result => nativePlayed = result,
                    error => nativeError = error);

                if (nativePlayed)
                {
                    onComplete?.Invoke(true);
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(nativeError))
                {
                    Debug.LogWarning("[SalesAgentTTS] Android native Gemini playback failed: " + nativeError);
                }
#endif

                AudioClip clip = CreatePcm16MonoClip(pcmBytes, mimeType);
                if (clip == null)
                {
                    onError?.Invoke("Gemini English TTS audio decode failed");
                    yield break;
                }

                EnsureAudioSource();
                DiagnosticStatus = "Speaking English (Gemini)";
                _audioSource.clip = clip;
                _audioSource.Play();
                _lastVoiceRoute = "Gemini Unity audio";

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

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator PlayGeminiPcmWithAndroidMediaPlayer(
            byte[] pcmBytes,
            string mimeType,
            Action<bool> onComplete,
            Action<string> onError)
        {
            onComplete?.Invoke(false);
            onError?.Invoke(null);

            string path = null;
            try
            {
                int sampleRate = ParsePcmSampleRate(mimeType);
                byte[] wavBytes = BuildWaveBytes(pcmBytes, sampleRate);
                path = Path.Combine(Application.temporaryCachePath, "closerxr_gemini_tts_" + DateTime.UtcNow.Ticks + ".wav");
                File.WriteAllBytes(path, wavBytes);

                PrepareAndroidAudioForPlayback();
                ReleaseAndroidMediaPlayer();
                _mediaPlayer = new AndroidJavaObject("android.media.MediaPlayer");
                ConfigureMediaPlayerAudioAttributes(_mediaPlayer);
                _mediaPlayer.Call("setDataSource", path);
                _mediaPlayer.Call("prepare");
                _mediaPlayer.Call("start");
                _lastVoiceRoute = "Gemini Android audio";
                DiagnosticStatus = "Speaking English (Android audio)";
            }
            catch (Exception e)
            {
                ReleaseAndroidMediaPlayer();
                TryDeleteFile(path);
                onError?.Invoke(e.Message);
                yield break;
            }

            float duration = EstimatePcmDuration(pcmBytes, ParsePcmSampleRate(mimeType));
            float timeout = Mathf.Clamp(duration + 1f, 1f, 30f);
            float elapsed = 0f;
            while (elapsed < timeout)
            {
                bool isPlaying = false;
                try
                {
                    isPlaying = _mediaPlayer != null && _mediaPlayer.Call<bool>("isPlaying");
                }
                catch
                {
                    break;
                }

                if (!isPlaying && elapsed > 0.25f)
                {
                    break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            ReleaseAndroidMediaPlayer();
            TryDeleteFile(path);
            onComplete?.Invoke(true);
        }
#endif

        private string BuildGeminiSpeechPrompt(string text)
        {
            if (string.IsNullOrWhiteSpace(geminiTtsPromptPrefix))
            {
                return text;
            }

            return geminiTtsPromptPrefix.TrimEnd() + " " + text.Trim();
        }

        private static byte[] ExtractGeminiPcmBytes(string responseText, out string mimeType)
        {
            mimeType = null;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            GeminiTtsResponse response = JsonUtility.FromJson<GeminiTtsResponse>(responseText);
            if (response?.candidates == null)
            {
                return null;
            }

            foreach (GeminiTtsCandidate candidate in response.candidates)
            {
                if (candidate?.content?.parts == null)
                {
                    continue;
                }

                foreach (GeminiTtsPart part in candidate.content.parts)
                {
                    if (string.IsNullOrWhiteSpace(part?.inlineData?.data))
                    {
                        continue;
                    }

                    mimeType = part.inlineData.mimeType;
                    try
                    {
                        return Convert.FromBase64String(part.inlineData.data);
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static AudioClip CreatePcm16MonoClip(byte[] pcmBytes, string mimeType)
        {
            int sampleCount = pcmBytes.Length / 2;
            if (sampleCount <= 0)
            {
                return null;
            }

            int frequency = ParsePcmSampleRate(mimeType);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * 2;
                short pcmSample = (short)(pcmBytes[offset] | (pcmBytes[offset + 1] << 8));
                samples[i] = Mathf.Clamp(pcmSample / 32768f, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("GeminiEnglishTTS", sampleCount, 1, frequency, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static byte[] BuildWaveBytes(byte[] pcmBytes, int sampleRate)
        {
            const short Channels = 1;
            const short BitsPerSample = 16;
            short blockAlign = (short)(Channels * BitsPerSample / 8);
            int byteRate = sampleRate * blockAlign;

            using (MemoryStream stream = new MemoryStream(44 + pcmBytes.Length))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + pcmBytes.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(Channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(BitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcmBytes.Length);
                writer.Write(pcmBytes);
                return stream.ToArray();
            }
        }

        private static float EstimatePcmDuration(byte[] pcmBytes, int sampleRate)
        {
            if (pcmBytes == null || pcmBytes.Length == 0 || sampleRate <= 0)
            {
                return 1f;
            }

            return pcmBytes.Length / 2f / sampleRate;
        }

        private static int ParsePcmSampleRate(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                return DefaultGeminiTtsSampleRate;
            }

            const string RateToken = "rate=";
            int rateIndex = mimeType.IndexOf(RateToken, StringComparison.OrdinalIgnoreCase);
            if (rateIndex < 0)
            {
                return DefaultGeminiTtsSampleRate;
            }

            int start = rateIndex + RateToken.Length;
            int end = start;
            while (end < mimeType.Length && char.IsDigit(mimeType[end]))
            {
                end++;
            }

            if (end <= start)
            {
                return DefaultGeminiTtsSampleRate;
            }

            return int.TryParse(mimeType.Substring(start, end - start), out int rate) && rate > 0
                ? rate
                : DefaultGeminiTtsSampleRate;
        }

        private static string BuildNoSoundStatus(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return "No sound: No English TTS available";
            }

            string normalized = error.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (normalized.Length > 80)
            {
                normalized = normalized.Substring(0, 77) + "...";
            }

            return "No sound: " + normalized;
        }

        private bool TryStartAndroidToneProbe(int durationMs, out string error)
        {
            error = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                PrepareAndroidAudioForPlayback();
                ReleaseAndroidToneGenerator();
                _toneGenerator = new AndroidJavaObject("android.media.ToneGenerator", 3, 100);
                using (AndroidJavaClass toneClass = new AndroidJavaClass("android.media.ToneGenerator"))
                {
                    int tone = toneClass.GetStatic<int>("TONE_PROP_BEEP");
                    return _toneGenerator.Call<bool>("startTone", tone, durationMs);
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                ReleaseAndroidToneGenerator();
                return false;
            }
#else
            error = "Android tone probe only runs on device";
            return false;
#endif
        }

        private static void PrepareAndroidAudioForPlayback()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity");
                    AndroidJavaObject audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                    const int StreamMusic = 3;
                    int maxVolume = audioManager.Call<int>("getStreamMaxVolume", StreamMusic);
                    audioManager.Call("setStreamVolume", StreamMusic, maxVolume, 0);
                    audioManager.Call<int>("requestAudioFocus", null, StreamMusic, 1);
                    audioManager.Dispose();
                    activity.Dispose();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SalesAgentTTS] Android audio focus setup failed: " + e.Message);
            }
#endif
        }

        private static void ConfigureMediaPlayerAudioAttributes(AndroidJavaObject mediaPlayer)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (mediaPlayer == null)
            {
                return;
            }

            try
            {
                AndroidJavaObject builder = new AndroidJavaObject("android.media.AudioAttributes$Builder");
                builder.Call<AndroidJavaObject>("setUsage", 1);
                builder.Call<AndroidJavaObject>("setContentType", 1);
                AndroidJavaObject attributes = builder.Call<AndroidJavaObject>("build");
                mediaPlayer.Call("setAudioAttributes", attributes);
                attributes.Dispose();
                builder.Dispose();
            }
            catch
            {
                mediaPlayer.Call("setAudioStreamType", 3);
            }
#endif
        }

        private void ReleaseAndroidMediaPlayer()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_mediaPlayer == null)
            {
                return;
            }

            try { _mediaPlayer.Call("stop"); } catch { }
            try { _mediaPlayer.Call("release"); } catch { }
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
#endif
        }

        private void ReleaseAndroidToneGenerator()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_toneGenerator == null)
            {
                return;
            }

            try { _toneGenerator.Call("stopTone"); } catch { }
            try { _toneGenerator.Call("release"); } catch { }
            _toneGenerator.Dispose();
            _toneGenerator = null;
#endif
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

        [Serializable]
        private sealed class GeminiTtsRequest
        {
            public GeminiTtsContent[] contents;
            public GeminiTtsGenerationConfig generationConfig;
        }

        [Serializable]
        private sealed class GeminiTtsGenerationConfig
        {
            public string[] responseModalities;
            public GeminiTtsSpeechConfig speechConfig;
        }

        [Serializable]
        private sealed class GeminiTtsSpeechConfig
        {
            public GeminiTtsVoiceConfig voiceConfig;
        }

        [Serializable]
        private sealed class GeminiTtsVoiceConfig
        {
            public GeminiTtsPrebuiltVoiceConfig prebuiltVoiceConfig;
        }

        [Serializable]
        private sealed class GeminiTtsPrebuiltVoiceConfig
        {
            public string voiceName;
        }

        [Serializable]
        private sealed class GeminiTtsContent
        {
            public GeminiTtsPart[] parts;
        }

        [Serializable]
        private sealed class GeminiTtsPart
        {
            public string text;
            public GeminiTtsInlineData inlineData;
        }

        [Serializable]
        private sealed class GeminiTtsInlineData
        {
            public string mimeType;
            public string data;
        }

        [Serializable]
        private sealed class GeminiTtsResponse
        {
            public GeminiTtsCandidate[] candidates;
        }

        [Serializable]
        private sealed class GeminiTtsCandidate
        {
            public GeminiTtsContent content;
        }
    }
}
