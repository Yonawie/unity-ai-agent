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
                {
                    object content;
                    var imgs = new List<string>();
                    if (msg.ImagePaths != null) imgs.AddRange(msg.ImagePaths);
                    if (msg.Role == "user" && request.ImagePaths != null && AgentSettings.Current.EnableVision)
                        imgs.AddRange(request.ImagePaths);

                    if (imgs.Count > 0 && AgentSettings.Current.EnableVision && msg.Role == "user")
                    {
                        var parts = new List<object>
                        {
                            new Dictionary<string, object> { ["type"] = "text", ["text"] = msg.Content ?? "" }
                        };
                        foreach (var p in imgs)
                        {
                            var b64 = VisionImageUtil.ToBase64(p);
                            if (b64 == null) continue;
                            parts.Add(new Dictionary<string, object>
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new Dictionary<string, object>
                                {
                                    ["url"] = $"data:{VisionImageUtil.Mime(p)};base64,{b64}"
                                }
                            });
                        }
                        content = parts;
                    }
                    else
                    {
                        content = msg.Content ?? "";
                    }

                    messages.Add(new Dictionary<string, object> { ["role"] = msg.Role, ["content"] = content });
                }

                var useStream = request.OnPartial != null && AgentSettings.Current.EnableStreaming;

                var body = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["temperature"] = request.Temperature,
                    ["messages"] = messages,
                    ["stream"] = useStream
                };

                if (request.JsonMode && !useStream)
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
                req.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(_apiKey))
                    req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
                req.timeout = _timeoutSeconds;

                OpenAiStreamHandler streamHandler = null;
                if (useStream)
                {
                    streamHandler = new OpenAiStreamHandler(chunk => request.OnPartial?.Invoke(chunk));
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
                    return new LLMResponse
                    {
                        Success = false,
                        Error = $"Request failed: {req.error}",
                        Raw = useStream ? streamHandler?.RawText : req.downloadHandler?.text,
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }

                if (useStream)
                {
                    var streamed = streamHandler?.Content ?? string.Empty;
                    AgentLogger.Llm("response", streamed);
                    if (string.IsNullOrWhiteSpace(streamed))
                    {
                        return new LLMResponse
                        {
                            Success = false,
                            Error = "LM Studio/OpenAI returned an empty streamed response. Disable 'Enable LLM Streaming' in Project Settings → AI Agent and try again.",
                            Raw = streamHandler?.RawText,
                            DurationMs = sw.ElapsedMilliseconds,
                            WasStreamed = true
                        };
                    }
                    return new LLMResponse
                    {
                        Success = true,
                        Content = streamed,
                        Raw = streamHandler.RawText,
                        DurationMs = sw.ElapsedMilliseconds,
                        WasStreamed = true
                    };
                }

                var raw = req.downloadHandler?.text;
                AgentLogger.Llm("response", raw);

                var root = AgentJson.ParseObject(raw);
                var choices = AgentJson.GetArray(root, "choices");
                string responseContent = null;
                if (choices != null && choices.Count > 0 && choices[0] is Dictionary<string, object> choice)
                {
                    var message = AgentJson.GetObject(choice, "message");
                    responseContent = AgentJson.GetString(message, "content");
                }

                return new LLMResponse
                {
                    Success = true,
                    Content = responseContent ?? raw,
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

        sealed class OpenAiStreamHandler : DownloadHandlerScript
        {
            readonly Action<string> _onPartial;
            readonly StringBuilder _lineBuf = new StringBuilder();
            readonly StringBuilder _content = new StringBuilder();
            readonly StringBuilder _raw = new StringBuilder();

            public string Content => _content.ToString();
            public string RawText => _raw.ToString();

            public OpenAiStreamHandler(Action<string> onPartial) : base(new byte[64 * 1024])
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
                    var line = s.Substring(0, nl).TrimEnd('\r');
                    _lineBuf.Remove(0, nl + 1);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var payload = line.Substring(5).Trim();
                    if (payload == "[DONE]") continue;
                    try
                    {
                        var obj = AgentJson.ParseObject(payload);
                        var choices = AgentJson.GetArray(obj, "choices");
                        if (choices == null || choices.Count == 0) continue;
                        if (choices[0] is not Dictionary<string, object> choice) continue;
                        var delta = AgentJson.GetObject(choice, "delta");
                        var piece = AgentJson.GetString(delta, "content");
                        if (string.IsNullOrEmpty(piece)) continue;
                        _content.Append(piece);
                        _onPartial?.Invoke(piece);
                    }
                    catch
                    {
                        // ignore bad chunk
                    }
                }

                return true;
            }
        }
    }

    public static class LLMProviderFactory
    {
        public static ILLMProvider CreateFromSettings()
        {
            var s = AgentSettings.Current;
            var provider = (s.Provider ?? "LMStudio").Trim();
            if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
            {
                return new ClaudeProvider(string.IsNullOrWhiteSpace(s.BaseUrl) || s.BaseUrl.Contains("11434")
                    ? "https://api.anthropic.com"
                    : s.BaseUrl, s.Model);
            }

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
            {
                return new GeminiProvider(string.IsNullOrWhiteSpace(s.BaseUrl) || s.BaseUrl.Contains("11434")
                    ? "https://generativelanguage.googleapis.com"
                    : s.BaseUrl, s.Model);
            }

            if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                return new OllamaProvider(s.BaseUrl, s.Model, s.RequestTimeoutSeconds);
            }

            // LMStudio / OpenAI / OpenAICompatible / default
            return new OpenAICompatibleProvider(s.BaseUrl, s.Model);
        }
    }
}
