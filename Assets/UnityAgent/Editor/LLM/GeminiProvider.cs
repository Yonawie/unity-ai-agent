using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityAgent.Editor.Logging;
using UnityAgent.Editor.Settings;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.LLM
{
    /// <summary>Google Gemini generateContent API.</summary>
    public sealed class GeminiProvider : ILLMProvider
    {
        public string Name => "Gemini";

        readonly string _baseUrl;
        readonly string _model;
        readonly string _apiKey;
        readonly int _timeoutSeconds;

        public GeminiProvider(string baseUrl = null, string model = null, string apiKey = null, int timeoutSeconds = 120)
        {
            _baseUrl = (baseUrl ?? "https://generativelanguage.googleapis.com")?.TrimEnd('/');
            _model = model ?? AgentSettings.Current.Model;
            _apiKey = apiKey
                      ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                      ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                      ?? Environment.GetEnvironmentVariable("UNITY_AGENT_API_KEY");
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : AgentSettings.Current.RequestTimeoutSeconds;
        }

        public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return (false, "GEMINI_API_KEY / GOOGLE_API_KEY is not set.");
            if (string.IsNullOrEmpty(_model))
                return (false, "Model is not set (e.g. gemini-2.0-flash).");
            return (true, $"Gemini provider ready. Model: {_model}");
        }

        public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var model = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model;
            if (string.IsNullOrWhiteSpace(model))
                return new LLMResponse { Success = false, Error = "Model is not set." };
            if (string.IsNullOrEmpty(_apiKey))
                return new LLMResponse { Success = false, Error = "GEMINI_API_KEY is not set." };

            try
            {
                var contents = new List<object>();
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    // Gemini: system_instruction separate field
                }

                foreach (var msg in request.Messages)
                {
                    var role = msg.Role == "assistant" ? "model" : "user";
                    var parts = new List<object>
                    {
                        new Dictionary<string, object> { ["text"] = msg.Content ?? "" }
                    };

                    var images = msg.ImagePaths ?? (role == "user" ? request.ImagePaths : null);
                    if (images != null)
                    {
                        foreach (var path in images)
                        {
                            var b64 = VisionImageUtil.ToBase64(path);
                            if (b64 == null) continue;
                            parts.Add(new Dictionary<string, object>
                            {
                                ["inline_data"] = new Dictionary<string, object>
                                {
                                    ["mime_type"] = VisionImageUtil.Mime(path),
                                    ["data"] = b64
                                }
                            });
                        }
                    }

                    contents.Add(new Dictionary<string, object> { ["role"] = role, ["parts"] = parts });
                }

                var body = new Dictionary<string, object>
                {
                    ["contents"] = contents,
                    ["generationConfig"] = new Dictionary<string, object>
                    {
                        ["temperature"] = request.Temperature,
                        ["maxOutputTokens"] = request.MaxTokens > 0 ? request.MaxTokens : 4096
                    }
                };
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                {
                    body["system_instruction"] = new Dictionary<string, object>
                    {
                        ["parts"] = new List<object>
                        {
                            new Dictionary<string, object> { ["text"] = request.SystemPrompt }
                        }
                    };
                }

                var url = $"{_baseUrl}/v1beta/models/{model}:generateContent?key={_apiKey}";
                var json = AgentJson.Serialize(body);
                AgentLogger.Llm("request", "[gemini] " + (json.Length > 2000 ? json.Substring(0, 2000) + "…" : json));

                using var req = new UnityWebRequest(url, "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = _timeoutSeconds;

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                sw.Stop();
                var raw = req.downloadHandler?.text;
                AgentLogger.Llm("response", raw);
                if (req.result != UnityWebRequest.Result.Success)
                    return new LLMResponse { Success = false, Error = req.error, Raw = raw, DurationMs = sw.ElapsedMilliseconds };

                var root = AgentJson.ParseObject(raw);
                var candidates = AgentJson.GetArray(root, "candidates");
                var sb = new StringBuilder();
                if (candidates != null && candidates.Count > 0 && candidates[0] is Dictionary<string, object> cand)
                {
                    var content = AgentJson.GetObject(cand, "content");
                    var parts = AgentJson.GetArray(content, "parts");
                    if (parts != null)
                    {
                        foreach (var p in parts)
                        {
                            if (p is Dictionary<string, object> pd)
                                sb.Append(AgentJson.GetString(pd, "text"));
                        }
                    }
                }

                return new LLMResponse
                {
                    Success = true,
                    Content = sb.Length > 0 ? sb.ToString() : raw,
                    Raw = raw,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                return new LLMResponse { Success = false, Error = "Cancelled.", DurationMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                return new LLMResponse { Success = false, Error = ex.Message, DurationMs = sw.ElapsedMilliseconds };
            }
        }
    }
}
