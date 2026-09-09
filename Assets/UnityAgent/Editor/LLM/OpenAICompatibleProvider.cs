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
            _baseUrl = NormalizeBaseUrl(baseUrl ?? AgentSettings.Current.BaseUrl);
            _model = model ?? AgentSettings.Current.Model;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("UNITY_AGENT_API_KEY");
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : AgentSettings.Current.RequestTimeoutSeconds;
        }

        static string NormalizeBaseUrl(string baseUrl)
        {
            var url = (baseUrl ?? "http://127.0.0.1:1234/v1").Trim().TrimEnd('/');
            // Common mistake: LM Studio root without /v1
            if (url.Contains(":1234") && !url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                url += "/v1";
            return url;
        }

        bool LooksLocal =>
            _baseUrl.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0 ||
            _baseUrl.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
            _baseUrl.Contains(":1234");

        public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var modelsUrl = _baseUrl.EndsWith("/v1", StringComparison.Ordinal)
                    ? _baseUrl + "/models"
                    : _baseUrl + "/v1/models";

                using var req = UnityWebRequest.Get(modelsUrl);
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
                    return (false, $"Endpoint unreachable ({modelsUrl}): {req.error}");

                var ids = ExtractModelIds(req.downloadHandler?.text);
                var configured = string.IsNullOrWhiteSpace(_model) ? "(not set)" : _model;
                if (ids.Count == 0)
                    return (true, $"Connected to {_baseUrl}. Configured model: {configured}. No models listed — load a model in LM Studio.");

                var preview = string.Join(", ", ids.GetRange(0, Math.Min(5, ids.Count)));
                var match = ids.Exists(id => string.Equals(id, _model, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(_model))
                    return (true, $"Connected. Models loaded: {preview}. Set Model in Settings to one of these (exact id).");
                if (!match)
                    return (true, $"Connected, but model '{_model}' is not in /models. Loaded: {preview}. Use an exact id from LM Studio.");

                return (true, $"Connected to {_baseUrl}. Model OK: {_model}");
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
                model = await TryResolveLoadedModelAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(model))
                {
                    return new LLMResponse
                    {
                        Success = false,
                        Error = "Model is not set. In LM Studio load a chat model, then copy its exact id into Agent Settings → Model (or click Test Connection)."
                    };
                }

                AgentLogger.Info("Auto-selected LM Studio model: " + model);
                try
                {
                    if (string.IsNullOrWhiteSpace(AgentSettings.Current.Model))
                    {
                        AgentSettings.Current.Model = model;
                        AgentSettings.Save();
                    }
                }
                catch { /* ignore */ }
            }

            try
            {
                // Local servers (LM Studio) often return empty replies when forced json_object.
                var allowJsonMode = request.JsonMode && !LooksLocal &&
                                    !string.Equals(AgentSettings.Current.Provider, "LMStudio", StringComparison.OrdinalIgnoreCase);

                var response = await SendOnceAsync(request, model, useJsonFormat: allowJsonMode, cancellationToken, sw);
                if (response.Success && string.IsNullOrWhiteSpace(response.Content) && allowJsonMode)
                {
                    AgentLogger.Warn("Empty content with response_format=json_object — retrying without it.");
                    response = await SendOnceAsync(request, model, useJsonFormat: false, cancellationToken, sw);
                }

                // If still empty but raw has text, surface a useful error.
                if (response.Success && string.IsNullOrWhiteSpace(response.Content))
                {
                    var snippet = Trim(response.Raw, 500);
                    response.Success = false;
                    response.Error =
                        "Model returned empty content. " +
                        "Check: 1) model loaded in LM Studio, 2) Model field matches exact id, " +
                        "3) Provider=LMStudio, Base URL=http://127.0.0.1:1234/v1, 4) streaming OFF. " +
                        (string.IsNullOrEmpty(snippet) ? "" : "Raw: " + snippet);
                }

                return response;
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

        async Task<LLMResponse> SendOnceAsync(
            LLMRequest request,
            string model,
            bool useJsonFormat,
            CancellationToken cancellationToken,
            Stopwatch sw)
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

            if (useJsonFormat && !useStream)
                body["response_format"] = new Dictionary<string, object> { ["type"] = "json_object" };

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
            // LM Studio can be slow on first token after loading.
            req.timeout = Math.Max(_timeoutSeconds, LooksLocal ? 180 : _timeoutSeconds);

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
            var rawBody = useStream ? streamHandler?.RawText : req.downloadHandler?.text;

            if (req.result != UnityWebRequest.Result.Success)
            {
                var apiErr = ExtractApiError(rawBody);
                return new LLMResponse
                {
                    Success = false,
                    Error = string.IsNullOrEmpty(apiErr)
                        ? $"Request failed: {req.error} ({endpoint})"
                        : $"Request failed: {apiErr}",
                    Raw = rawBody,
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
                        Error = "Empty streamed response. Turn OFF 'Enable LLM Streaming' in Project Settings → AI Agent.",
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

            AgentLogger.Llm("response", rawBody);
            var root = AgentJson.ParseObject(rawBody);
            var apiError = ExtractApiError(rawBody);
            if (!string.IsNullOrEmpty(apiError) && (root == null || AgentJson.GetArray(root, "choices") == null))
            {
                return new LLMResponse
                {
                    Success = false,
                    Error = apiError,
                    Raw = rawBody,
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            var responseContent = ExtractChoiceContent(root);
            return new LLMResponse
            {
                Success = true,
                Content = responseContent ?? string.Empty,
                Raw = rawBody,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        async Task<string> TryResolveLoadedModelAsync(CancellationToken cancellationToken)
        {
            try
            {
                var modelsUrl = _baseUrl.EndsWith("/v1", StringComparison.Ordinal)
                    ? _baseUrl + "/models"
                    : _baseUrl + "/v1/models";
                using var req = UnityWebRequest.Get(modelsUrl);
                if (!string.IsNullOrEmpty(_apiKey))
                    req.SetRequestHeader("Authorization", "Bearer " + _apiKey);
                req.timeout = 10;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success) return null;
                var ids = ExtractModelIds(req.downloadHandler?.text);
                return ids.Count > 0 ? ids[0] : null;
            }
            catch
            {
                return null;
            }
        }

        static List<string> ExtractModelIds(string raw)
        {
            var ids = new List<string>();
            var root = AgentJson.ParseObject(raw);
            var data = AgentJson.GetArray(root, "data");
            if (data == null) return ids;
            foreach (var item in data)
            {
                if (item is not Dictionary<string, object> d) continue;
                var id = AgentJson.GetString(d, "id");
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
            }
            return ids;
        }

        static string ExtractChoiceContent(Dictionary<string, object> root)
        {
            if (root == null) return null;
            var choices = AgentJson.GetArray(root, "choices");
            if (choices == null || choices.Count == 0) return null;
            if (choices[0] is not Dictionary<string, object> choice) return null;

            var message = AgentJson.GetObject(choice, "message") ?? AgentJson.GetObject(choice, "delta");
            if (message == null)
            {
                // Some gateways put text on the choice itself.
                return AgentJson.GetString(choice, "text");
            }

            var text = CoerceContent(message, "content");
            if (!string.IsNullOrWhiteSpace(text)) return text;

            // Reasoning / thinking models sometimes leave content empty.
            text = CoerceContent(message, "reasoning_content");
            if (!string.IsNullOrWhiteSpace(text)) return text;

            text = CoerceContent(message, "reasoning");
            if (!string.IsNullOrWhiteSpace(text)) return text;

            // OpenAI-style tool_calls without text — serialize for the agent parser.
            var toolCalls = AgentJson.GetArray(message, "tool_calls");
            if (toolCalls != null && toolCalls.Count > 0)
                return AgentJson.Serialize(new Dictionary<string, object> { ["tool_calls"] = toolCalls });

            return AgentJson.GetString(choice, "text");
        }

        static string CoerceContent(Dictionary<string, object> obj, string key)
        {
            if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
                return null;

            if (value is string s)
                return s;

            if (value is List<object> parts)
            {
                var sb = new StringBuilder();
                foreach (var part in parts)
                {
                    if (part is string ps)
                    {
                        sb.Append(ps);
                        continue;
                    }
                    if (part is not Dictionary<string, object> d) continue;
                    var t = AgentJson.GetString(d, "text") ?? AgentJson.GetString(d, "content");
                    if (!string.IsNullOrEmpty(t)) sb.Append(t);
                }
                return sb.ToString();
            }

            return Convert.ToString(value);
        }

        static string ExtractApiError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var root = AgentJson.ParseObject(raw);
            if (root == null) return null;

            var errObj = AgentJson.GetObject(root, "error");
            if (errObj != null)
            {
                var msg = AgentJson.GetString(errObj, "message")
                          ?? AgentJson.GetString(errObj, "msg")
                          ?? AgentJson.Serialize(errObj);
                return msg;
            }

            return AgentJson.GetString(root, "error") ?? AgentJson.GetString(root, "message");
        }

        static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace('\n', ' ');
            return s.Length <= max ? s : s.Substring(0, max) + "…";
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
                        var delta = AgentJson.GetObject(choice, "delta") ?? choice;
                        var piece = CoerceContent(delta, "content");
                        if (string.IsNullOrEmpty(piece))
                            piece = CoerceContent(delta, "reasoning_content");
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
