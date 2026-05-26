using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class GeminiSalesClient : MonoBehaviour
    {
        [SerializeField] private string apiKeyOverride = "";
        [SerializeField] private string apiKeyEnvironmentVariable = "GEMINI_API_KEY";
        [SerializeField] private string model = "gemini-2.5-flash";
        [SerializeField, Range(0f, 2f)] private float temperature = 0.9f;
        [SerializeField] private int maxOutputTokens = 180;

        private const string EndpointTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        public bool HasApiKey => !string.IsNullOrWhiteSpace(GetApiKey());
        public string Model => model;

        public IEnumerator GenerateFromText(
            string userText,
            string prompt,
            Action<string> onSuccess,
            Action<string> onError)
        {
            GeminiPart part = new GeminiPart
            {
                text = $"{prompt}\n\nUser response: {userText}\n\nAnswer as the salesperson in two punchy sentences."
            };

            GeminiRequest request = CreateRequest(new[] { part });
            yield return SendRequest(request, onSuccess, onError);
        }

        public IEnumerator GenerateFromAudio(
            AudioClip audioClip,
            string prompt,
            Action<string> onSuccess,
            Action<string> onError)
        {
            if (audioClip == null)
            {
                onError?.Invoke("No recorded audio clip was provided.");
                yield break;
            }

            byte[] wavBytes = WavEncoder.Encode(audioClip);
            GeminiPart instructions = new GeminiPart
            {
                text = $"{prompt}\n\nFirst infer what the user said from this audio. Then answer as the salesperson in two punchy sentences."
            };

            GeminiPart audioPart = new GeminiPart
            {
                inlineData = new GeminiInlineData
                {
                    mimeType = "audio/wav",
                    data = Convert.ToBase64String(wavBytes)
                }
            };

            GeminiRequest request = CreateRequest(new[] { instructions, audioPart });
            yield return SendRequest(request, onSuccess, onError);
        }

        private GeminiRequest CreateRequest(GeminiPart[] parts)
        {
            return new GeminiRequest
            {
                contents = new[]
                {
                    new GeminiContent
                    {
                        role = "user",
                        parts = parts
                    }
                },
                generationConfig = new GeminiGenerationConfig
                {
                    temperature = temperature,
                    maxOutputTokens = maxOutputTokens
                }
            };
        }

        private IEnumerator SendRequest(GeminiRequest request, Action<string> onSuccess, Action<string> onError)
        {
            string apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onError?.Invoke("Missing Gemini API key. Set GEMINI_API_KEY or paste a key on GeminiSalesClient for device testing.");
                yield break;
            }

            string json = JsonUtility.ToJson(request);
            byte[] body = Encoding.UTF8.GetBytes(json);
            string endpoint = string.Format(EndpointTemplate, model);

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
                    onError?.Invoke(message);
                    yield break;
                }

                GeminiResponse response = JsonUtility.FromJson<GeminiResponse>(webRequest.downloadHandler.text);
                string text = ExtractText(response);
                if (string.IsNullOrWhiteSpace(text))
                {
                    onError?.Invoke("Gemini returned no text.");
                    yield break;
                }

                onSuccess?.Invoke(text.Trim());
            }
        }

        private string GetApiKey()
        {
            if (!string.IsNullOrWhiteSpace(apiKeyOverride))
            {
                return apiKeyOverride.Trim();
            }

            return Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
        }

        private static string ExtractText(GeminiResponse response)
        {
            if (response?.candidates == null)
            {
                return "";
            }

            foreach (GeminiCandidate candidate in response.candidates)
            {
                if (candidate?.content?.parts == null)
                {
                    continue;
                }

                foreach (GeminiPart part in candidate.content.parts)
                {
                    if (!string.IsNullOrWhiteSpace(part.text))
                    {
                        return part.text;
                    }
                }
            }

            return "";
        }

        [Serializable]
        private sealed class GeminiRequest
        {
            public GeminiContent[] contents;
            public GeminiGenerationConfig generationConfig;
        }

        [Serializable]
        private sealed class GeminiContent
        {
            public string role;
            public GeminiPart[] parts;
        }

        [Serializable]
        private sealed class GeminiPart
        {
            public string text;
            public GeminiInlineData inlineData;
        }

        [Serializable]
        private sealed class GeminiInlineData
        {
            public string mimeType;
            public string data;
        }

        [Serializable]
        private sealed class GeminiGenerationConfig
        {
            public float temperature;
            public int maxOutputTokens;
        }

        [Serializable]
        private sealed class GeminiResponse
        {
            public GeminiCandidate[] candidates;
        }

        [Serializable]
        private sealed class GeminiCandidate
        {
            public GeminiContent content;
        }
    }
}
