using System;
using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] private int maxHistoryTurns = 10;

        private const string EndpointTemplate =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        private readonly List<GeminiContent> _history = new List<GeminiContent>();

        public bool HasApiKey => !string.IsNullOrWhiteSpace(GetApiKey());
        public string Model => model;

        // Seed history with the opening pitch so Gemini knows what was already said.
        public void InitHistory(string systemPrompt, string openingLine)
        {
            _history.Clear();
            _history.Add(new GeminiContent
            {
                role = "user",
                parts = new[] { new GeminiPart { text = $"{systemPrompt}\n\nBegin your opening sales pitch." } }
            });
            _history.Add(new GeminiContent
            {
                role = "model",
                parts = new[] { new GeminiPart { text = openingLine } }
            });
        }

        public void ResetHistory() => _history.Clear();

        public IEnumerator GenerateFromText(
            string userText,
            string prompt,
            Action<string> onSuccess,
            Action<string> onError)
        {
            // First real user turn includes the system prompt so Gemini knows its role.
            string formattedText = _history.Count == 0
                ? $"{prompt}\n\nUser response: {userText}\n\nAnswer as the salesperson in two punchy sentences."
                : userText;

            List<GeminiContent> allContents = new List<GeminiContent>(_history);
            allContents.Add(new GeminiContent
            {
                role = "user",
                parts = new[] { new GeminiPart { text = formattedText } }
            });

            GeminiRequest request = CreateRequest(allContents.ToArray());

            yield return SendRequest(
                request,
                response =>
                {
                    _history.Add(new GeminiContent
                    {
                        role = "user",
                        parts = new[] { new GeminiPart { text = formattedText } }
                    });
                    _history.Add(new GeminiContent
                    {
                        role = "model",
                        parts = new[] { new GeminiPart { text = response } }
                    });
                    PruneHistory();
                    onSuccess?.Invoke(response);
                },
                onError);
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

            string instructionText = _history.Count == 0
                ? $"{prompt}\n\nFirst infer what the user said from this audio. Then answer as the salesperson in two punchy sentences."
                : "First infer what the user said from this audio. Then answer as the salesperson in two punchy sentences.";

            GeminiPart instructions = new GeminiPart { text = instructionText };
            GeminiPart audioPart = new GeminiPart
            {
                inlineData = new GeminiInlineData
                {
                    mimeType = "audio/wav",
                    data = Convert.ToBase64String(wavBytes)
                }
            };

            List<GeminiContent> allContents = new List<GeminiContent>(_history);
            allContents.Add(new GeminiContent
            {
                role = "user",
                parts = new[] { instructions, audioPart }
            });

            GeminiRequest request = CreateRequest(allContents.ToArray());

            yield return SendRequest(
                request,
                response =>
                {
                    // Store a text placeholder in history — we send audio to Gemini but
                    // history only needs text for subsequent context.
                    _history.Add(new GeminiContent
                    {
                        role = "user",
                        parts = new[] { new GeminiPart { text = "[voice input]" } }
                    });
                    _history.Add(new GeminiContent
                    {
                        role = "model",
                        parts = new[] { new GeminiPart { text = response } }
                    });
                    PruneHistory();
                    onSuccess?.Invoke(response);
                },
                onError);
        }

        private void PruneHistory()
        {
            int maxEntries = maxHistoryTurns * 2;
            while (_history.Count > maxEntries)
            {
                _history.RemoveAt(0);
                if (_history.Count > 0)
                {
                    _history.RemoveAt(0);
                }
            }
        }

        private GeminiRequest CreateRequest(GeminiContent[] contents)
        {
            return new GeminiRequest
            {
                contents = contents,
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
                onError?.Invoke(
                    "Missing Gemini API key. Set GEMINI_API_KEY env var, paste a key into GeminiSalesClient, " +
                    "or drop your key into Assets/StreamingAssets/gemini_key.txt.");
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

            string envKey = Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                return envKey.Trim();
            }

            return ReadKeyFromStreamingAssets();
        }

        private static string ReadKeyFromStreamingAssets()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "gemini_key.txt");
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            string contents = System.IO.File.ReadAllText(path).Trim();
            return contents.StartsWith("YOUR_GEMINI", StringComparison.OrdinalIgnoreCase) ? null : contents;
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
