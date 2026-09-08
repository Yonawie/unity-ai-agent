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
    /// <summary>
    /// OpenAI-compatible Chat Completions provider (OpenAI, LM Studio, Groq, local gateways, etc.).
    /// API key is read from environment variable UNITY_AGENT_API_KEY (never logged).
    /// </summary>
    public sealed class OpenAICompatibleProvider : ILLMProvider
    {
        public string Name => "OpenAICompatible";

        readonly string _baseUrl;
        readonly string _model;
        readonly string _apiKey;
        readonly int _timeoutSeconds;

        public OpenAICompatibleProvider(string baseUrl = null, string model = null, string apiKey = null, int timeoutSeconds = 120)
        {
            _baseUrl = (baseUrl ?? AgentSettings.Current.BaseUrl)?.TrimEnd('/') ?? "https://api.openai.com/v1";
            _model = model ?? AgentSettings.Current.Model;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("UNITY_AGENT_API_KEY");
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : AgentSettings.Current.RequestTimeoutSeconds;
        }

        public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var url = _baseUrl.EndsWith("/v1", StringComparison.Ordinal)
                    ? _baseUrl + "/models"
                    : _baseUrl + "/v1/models";

                using var req = UnityWebRequest.Get(url);
                if (!string.IsNullOrEmpty(_apiKey))
                    req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
                req.timeout = Math.Min(_timeoutSeconds, 15);

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (req.result != UnityWebRequest.Result.Success)
                    return (false, $"OpenAI-compatible endpoint unreachable: {req.error}");

                return (true, $"Connected to {_baseUrl}. Configured model: {(string.IsNullOrEmpty(_model) ? "(not set)" : _model)}");
            }
            catch (OperationCanceledException)
            {
                return (false, "Connection test cancelled.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var model = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model;
            if (string.IsNullOrWhiteSpace(model))
            {
                return new LLMResponse
                {
                    Success = false,
                    Error = "Model is not set."
                };
            }

            try
            {
                var messages = new List<object>();
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    messages.Add(new Dictionary<string, object> { ["role"] = "system", ["content"] = request.SystemPrompt });

                foreach (var msg in request.Messages)
                    messages.Add(new Dictionary<string, object> { ["role"] = msg.Role, ["content"] = msg.Content ?? "" });

                var body = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["temperature"] = request.Temperature,
                    ["messages"] = messages
                };

                if (request.JsonMode)
                {
                    body["response_format"] = new Dictionary<string, object> { ["type"] = "json_object" };
                }

                var endpoint = _baseUrl.Contains("/chat/completions")
                    ? _baseUrl
                    : (_baseUrl.EndsWith("/v1", StringComparison.Ordinal)
                        ? _baseUrl + "/chat/completions"
                        : _baseUrl + "/v1/chat/completions");

                var json = AgentJson.Serialize(body);
                AgentLogger.Llm("request", json);

                using var req = new UnityWebRequest(endpoint, "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(_apiKey))
                    req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
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
                {
                    return new LLMResponse
                    {
                        Success = false,
                        Error = $"Request failed: {req.error}",
                        Raw = raw,
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                var root = AgentJson.ParseObject(raw);
                var choices = AgentJson.GetArray(root, "choices");
                string content = null;
                if (choices != null && choices.Count > 0 && choices[0] is Dictionary<string, object> choice)
                {
                    var message = AgentJson.GetObject(choice, "message");
                    content = AgentJson.GetString(message, "content");
                }

                return new LLMResponse
                {
                    Success = true,
                    Content = content ?? raw,
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
                AgentLogger.Error($"OpenAICompatibleProvider error: {ex}");
                return new LLMResponse { Success = false, Error = ex.Message, DurationMs = sw.ElapsedMilliseconds };
            }
        }
    }

    public static class LLMProviderFactory
    {
        public static ILLMProvider CreateFromSettings()
        {
            var s = AgentSettings.Current;
            var provider = (s.Provider ?? "Ollama").Trim();
            if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("LMStudio", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenAICompatibleProvider(s.BaseUrl, s.Model);
            }

            return new OllamaProvider(s.BaseUrl, s.Model, s.RequestTimeoutSeconds);
        }
    }
}
