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

                var models = new List<string>();
                var root = AgentJson.ParseObject(req.downloadHandler.text);
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

            var useStream = request.OnPartial != null && AgentSettings.Current.EnableStreaming;

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
                    ["stream"] = useStream,
                    ["messages"] = messages,
                    ["options"] = new Dictionary<string, object>
                    {
                        ["temperature"] = request.Temperature
                    }
                };

                if (request.JsonMode)
                    body["format"] = "json";

                var json = AgentJson.Serialize(body);
                AgentLogger.Llm("request", json);

                using var req = new UnityWebRequest(_baseUrl + "/api/chat", "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = _timeoutSeconds;

                OllamaStreamHandler streamHandler = null;
                if (useStream)
                {
                    streamHandler = new OllamaStreamHandler(chunk => request.OnPartial?.Invoke(chunk));
                    req.downloadHandler = streamHandler;
                }
                else
                {
                    req.downloadHandler = new DownloadHandlerBuffer();
                }

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                sw.Stop();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    var errBody = useStream ? streamHandler?.RawText : req.downloadHandler?.text;
                    return new LLMResponse
                    {
                        Success = false,
                        Error = $"Ollama request failed: {req.error}",
                        Raw = errBody,
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                if (useStream)
                {
                    var content = streamHandler.Content;
                    AgentLogger.Llm("response", content);
                    return new LLMResponse
                    {
                        Success = true,
                        Content = content,
                        Raw = streamHandler.RawText,
                        DurationMs = sw.ElapsedMilliseconds,
                        WasStreamed = true
                    };
                }

                var raw = req.downloadHandler?.text;
                AgentLogger.Llm("response", raw);
                var root = AgentJson.ParseObject(raw);
                var message = AgentJson.GetObject(root, "message");
                var nonStreamContent = AgentJson.GetString(message, "content", raw);
                return new LLMResponse
                {
                    Success = true,
                    Content = nonStreamContent,
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

        sealed class OllamaStreamHandler : DownloadHandlerScript
        {
            readonly Action<string> _onPartial;
            readonly StringBuilder _lineBuf = new StringBuilder();
            readonly StringBuilder _content = new StringBuilder();
            readonly StringBuilder _raw = new StringBuilder();

            public string Content => _content.ToString();
            public string RawText => _raw.ToString();

            public OllamaStreamHandler(Action<string> onPartial) : base(new byte[64 * 1024])
            {
                _onPartial = onPartial;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength == 0) return false;
                var text = Encoding.UTF8.GetString(data, 0, dataLength);
                _raw.Append(text);
                _lineBuf.Append(text);

                while (true)
                {
                    var s = _lineBuf.ToString();
                    var nl = s.IndexOf('\n');
                    if (nl < 0) break;
                    var line = s.Substring(0, nl).Trim();
                    _lineBuf.Remove(0, nl + 1);
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        var obj = AgentJson.ParseObject(line);
                        var message = AgentJson.GetObject(obj, "message");
                        var piece = AgentJson.GetString(message, "content", null);
                        if (!string.IsNullOrEmpty(piece))
                        {
                            _content.Append(piece);
                            _onPartial?.Invoke(piece);
                        }
                    }
                    catch
                    {
                        // ignore malformed chunk
                    }
                }

                return true;
            }
        }
    }
}
