using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnityAgent.Editor.LLM
{
    public sealed class LLMMessage
    {
        public string Role;
        public string Content;

        public LLMMessage() { }
        public LLMMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    public sealed class LLMRequest
    {
        public string Model;
        public float Temperature = 0.2f;
        public int MaxTokens = 4096;
        public List<LLMMessage> Messages = new List<LLMMessage>();
        public string SystemPrompt;
        public bool JsonMode;
        /// <summary>Optional streaming callback for partial text chunks.</summary>
        public Action<string> OnPartial;
    }

    public sealed class LLMResponse
    {
        public bool Success;
        public string Content;
        public string Error;
        public string Raw;
        public long DurationMs;
        public bool WasStreamed;
    }

    public interface ILLMProvider
    {
        string Name { get; }
        Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken);
        Task<(bool ok, string message)> TestConnectionAsync(CancellationToken cancellationToken);
    }
}
