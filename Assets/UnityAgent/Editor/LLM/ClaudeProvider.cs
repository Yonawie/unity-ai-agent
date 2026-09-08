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
    /// <summary>Anthropic Claude Messages API.</summary>
    public sealed class ClaudeProvider : ILLMProvider
    {
        public string Name => "Claude";

        readonly string _baseUrl;
        readonly string _model;
        readonly string _apiKey;
        readonly int _timeoutSeconds;

        public ClaudeProvider(string baseUrl = null, string model = null, string apiKey = null, int timeoutSeconds = 120)
        {
            _baseUrl = (baseUrl ?? "https://api.anthropic.com")?.TrimEnd('/');
            _model = model ?? AgentSettings.Current.Model;
            _apiKey = apiKey
                      ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                      ?? Environment.GetEnvironmentVariable("UNITY_AGENT_API_KEY");
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : AgentSettings.Current.RequestTimeoutSeconds;
        }

        public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return (false, "ANTHROPIC_API_KEY / UNITY_AGENT_API_KEY is not set.");
            return (true, $"Claude provider ready. Model: {(string.IsNullOrEmpty(_model) ? "(not set)" : _model)}");
        }

        public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var model = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model;
            if (string.IsNullOrWhiteSpace(model))
                return new LLMResponse { Success = false, Error = "Model is not set." };
            if (string.IsNullOrEmpty(_apiKey))
                return new LLMResponse { Success = false, Error = "ANTHROPIC_API_KEY is not set." };

            try
            {
                var messages = new List<object>();
                foreach (var msg in request.Messages)
                {
                    if (msg.Role == "system") continue;
                    var role = msg.Role == "tool" ? "user" : msg.Role;
                    var contentParts = new List<object>();
                    contentParts.Add(new Dictionary<string, object> { ["type"] = "text", ["text"] = msg.Content ?? "" });

                    var images = msg.ImagePaths ?? request.ImagePaths;
                    if (images != null && role == "user")
                    {
                        foreach (var path in images)
                        {
                            var b64 = VisionImageUtil.ToBase64(path);
                            if (b64 == null) continue;
                            contentParts.Add(new Dictionary<string, object>
                            {
                                ["type"] = "image",
                                ["source"] = new Dictionary<string, object>
                                {
                                    ["type"] = "base64",
                                    ["media_type"] = VisionImageUtil.Mime(path),
                                    ["data"] = b64
                                }
                            });
                        }
                    }

                    messages.Add(new Dictionary<string, object> { ["role"] = role, ["content"] = contentParts });
                }

                // Attach request-level images to last user message if none on messages.
                if (request.ImagePaths != null && request.ImagePaths.Count > 0 && messages.Count > 0)
                {
                    // already attached above via request.ImagePaths for user roles
                }

                var body = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["max_tokens"] = request.MaxTokens > 0 ? request.MaxTokens : 4096,
                    ["temperature"] = request.Temperature,
                    ["messages"] = messages
                };
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    body["system"] = request.SystemPrompt;

                var json = AgentJson.Serialize(body);
                AgentLogger.Llm("request", "[claude] " + Truncate(json));

                using var req = new UnityWebRequest(_baseUrl + "/v1/messages", "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("x-api-key", _apiKey);
                req.SetRequestHeader("anthropic-version", "2023-06-01");
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
                var contentArr = AgentJson.GetArray(root, "content");
                var sb = new StringBuilder();
                if (contentArr != null)
                {
                    foreach (var part in contentArr)
                    {
                        if (part is Dictionary<string, object> d)
                            sb.Append(AgentJson.GetString(d, "text"));
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

        static string Truncate(string s) => string.IsNullOrEmpty(s) || s.Length < 2000 ? s : s.Substring(0, 2000) + "…";
    }
}
