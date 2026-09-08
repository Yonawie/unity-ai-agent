using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityAgent.Editor.Logging;
using UnityAgent.Editor.Settings;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.LLM
{
    public sealed class OllamaProvider : ILLMProvider
    {
        public string Name => "Ollama";

        readonly string _baseUrl;
        readonly string _model;
        readonly int _timeoutSeconds;

        public OllamaProvider(string baseUrl = null, string model = null, int timeoutSeconds = 120)
        {
            _baseUrl = (baseUrl ?? AgentSettings.Current.BaseUrl)?.TrimEnd('/') ?? "http://localhost:11434";
            _model = model ?? AgentSettings.Current.Model;
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : AgentSettings.Current.RequestTimeoutSeconds;
        }

        public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var url = _baseUrl + "/api/tags";
                using var req = UnityWebRequest.Get(url);
                req.timeout = Math.Min(_timeoutSeconds, 15);
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (req.result != UnityWebRequest.Result.Success)
                    return (false, $"Ollama unreachable at {_baseUrl}: {req.error}");

                var json = req.downloadHandler.text;
                var models = new List<string>();
                var root = AgentJson.ParseObject(json);
                if (root != null && root.TryGetValue("models", out var m) && m is List<object> list)
                {
                    foreach (var item in list)
                    {
                        if (item is Dictionary<string, object> dict)
                        {
                            var name = AgentJson.GetString(dict, "name");
                            if (!string.IsNullOrEmpty(name)) models.Add(name);
                        }
                    }
                }

                var modelHint = string.IsNullOrEmpty(_model)
                    ? "No model configured yet."
                    : $"Configured model: {_model}";
                return (true, $"Connected to Ollama. Models: {(models.Count == 0 ? "(none listed)" : string.Join(", ", models))}. {modelHint}");
            }
            catch (OperationCanceledException)
            {
                return (false, "Connection test cancelled.");
            }
            catch (Exception ex)
            {
                return (false, $"Ollama connection failed: {ex.Message}");
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
                    Error = "Model is not set. Configure it in Project Settings → AI Agent or the AI Agent window."
                };
            }

            try
            {
                var messages = new List<object>();
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    messages.Add(new Dictionary<string, object> { ["role"] = "system", ["content"] = request.SystemPrompt });

                foreach (var msg in request.Messages)
                {
                    messages.Add(new Dictionary<string, object>
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content ?? string.Empty
                    });
                }

                var body = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["stream"] = false,
                    ["messages"] = messages,
                    ["options"] = new Dictionary<string, object>
                    {
                        ["temperature"] = request.Temperature
                    }
                };

                if (request.JsonMode)
                {
                    body["format"] = "json";
                }

                var json = AgentJson.Serialize(body);
                AgentLogger.Llm("request", json);

                using var req = new UnityWebRequest(_baseUrl + "/api/chat", "POST");
                var payload = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(payload);
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
                {
                    return new LLMResponse
                    {
                        Success = false,
                        Error = $"Ollama request failed: {req.error}",
                        Raw = raw,
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                var root = AgentJson.ParseObject(raw);
                var message = AgentJson.GetObject(root, "message");
                var content = AgentJson.GetString(message, "content", raw);

                return new LLMResponse
                {
                    Success = true,
                    Content = content,
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
                AgentLogger.Error($"OllamaProvider error: {ex}");
                return new LLMResponse
                {
                    Success = false,
                    Error = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }
        }
    }
}
