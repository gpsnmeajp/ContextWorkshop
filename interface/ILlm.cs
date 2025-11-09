using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using ContextWorkshop.Interface;
using Microsoft.Extensions.AI;

namespace ContextWorkshop.Interface
{
    public class LlmResponse
    {
        public Common.Role Role { get; set; }
        public string Content { get; set; }
        public bool IsComplete { get; set; }

        public LlmResponse(Common.Role role, string content, bool isComplete)
        {
            Role = role;
            Content = content;
            IsComplete = isComplete;
        }

        public override string ToString()
        {
            return $"{Role}: {Content} (Complete: {IsComplete})";
        }
    }
    public interface ILlm
    {
        Task InitializeAsync();
        Task ResetAsync();
        Task GenerateResponseAsync(string prompt, string systemMessage, IList<AITool> tools, Action<LlmResponse> onProgress, Action<LlmResponse> onComplete);
    }
}