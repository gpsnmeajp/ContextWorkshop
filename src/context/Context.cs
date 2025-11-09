using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ContextWorkshop.Interface;
using Microsoft.Extensions.Primitives;
using Tiktoken;

namespace ContextWorkshop
{
    public class Context : IContext, IDisposable
    {
        List<ContextItem> contextItems = new List<ContextItem>();
        Dictionary<string, string> facts = new Dictionary<string, string>();
        private readonly ILlm _llm;
        private readonly ITool _tool;

        public Context(ILlm llm, ITool tool)
        {
            _llm = llm;
            _tool = tool;
        }

        public async Task InitializeAsync()
        {
            contextItems.Clear();

            // 簡易的なコンテキスト管理としてファイルから読みだす。
            if (File.Exists("assets/context_history.json"))
            {
                string fileContent = await File.ReadAllTextAsync("assets/context_history.json");
                var items = JsonSerializer.Deserialize<List<ContextItem>>(fileContent);
                if (items != null)
                {
                    contextItems = items;
                }
            }
            // ファクトも同様に読みだす
            if (File.Exists("assets/facts.json"))
            {
                string fileContent = await File.ReadAllTextAsync("assets/facts.json");
                var loadedFacts = JsonSerializer.Deserialize<Dictionary<string, string>>(fileContent);
                if (loadedFacts != null)
                {
                    facts = loadedFacts;
                }
            }
        }

        public async Task ResetAsync()
        {
            contextItems.Clear();
        }
        public async Task<List<ContextItem>> GetContextItemsAsync()
        {
            return contextItems.ToList();
        }

        public async Task AddContextItemAsync(ContextItem item)
        {
            contextItems.Add(item);

            // 簡易的なコンテキスト管理としてファイルに保存する
            await File.WriteAllTextAsync("assets/context_history.json", Serializer.JsonSerialize(contextItems));
            // ついでにファクトも保存する
            await File.WriteAllTextAsync("assets/facts.json", Serializer.JsonSerialize(facts));
        }

        public async Task DeleteContextItemAsync(Guid id)
        {
            contextItems.RemoveAll(item => item.Id == id);
        }

        public async Task EditContextItemAsync(ContextItem item)
        {
            var index = contextItems.FindIndex(i => i.Id == item.Id);
            if (index != -1)
            {
                contextItems[index] = item;
            }
            else
            {
                throw new Exception("Context item not found");
            }
        }

        public async Task<string> GetSystemMessageAsync()
        {
            // もし"assets/system_prompt.txt"が存在しない場合は新規作成する
            Directory.CreateDirectory("assets");
            if (!File.Exists("assets/system_prompt.txt"))
            {
                string defaultPrompt = "あなたは親切なアシスタントです。";
                await File.WriteAllTextAsync("assets/system_prompt.txt", defaultPrompt);
            }
            string systemMessage = await File.ReadAllTextAsync("assets/system_prompt.txt");

            var contextItems = await GetContextItemsAsync();
            systemMessage += "\n\n";
            systemMessage += "# ファクト";
            foreach (var kvp in facts)
            {
                systemMessage += $"\n{kvp.Key}: {kvp.Value}";
            }

            systemMessage += "\n\n";
            systemMessage += "# 会話履歴";

            // 最後の1つのユーザーの発言を除いて全て連結する
            // Role: Content の形式で連結する
            var lastUserMessage = contextItems.LastOrDefault();
            var count = lastUserMessage?.Role == Common.Role.User ? (contextItems.Count - 1) : contextItems.Count;

            var conversationHistory = contextItems.Take(count);
            foreach (var item in conversationHistory)
            {
                if (item.Role == Common.Role.User)
                {
                    // ローカル時間でタイムスタンプを追加
                    systemMessage += $"\n{item.Role}: {item.Content} [{item.Timestamp.ToLocalTime()}]";
                }
                else
                {
                    systemMessage += $"\n{item.Role}: {item.Content}";
                }
            }

            systemMessage += "\n\n";
            systemMessage += "# 現在時刻";
            systemMessage += $"\n{DateTime.Now.ToLocalTime()}";

            return systemMessage;
        }
        
        public async Task<int> GetSystemMessageTokenCountAsync()
        {
            return ModelToEncoder.For("gpt-4o").CountTokens(await GetSystemMessageAsync());
        }

        public async Task GenerateAsync(Action onProgress, Action onComplete)
        {
            string systemMessage = await GetSystemMessageAsync();

            // 最後のユーザーメッセージをプロンプトとして使用
            var lastUserMessage = contextItems.LastOrDefault();
            string userPrompt = lastUserMessage?.Content ?? "";

            // 最後のメッセージがユーザーではない場合は例外
            if (lastUserMessage == null || lastUserMessage.Role != Common.Role.User)
            {
                throw new Exception("最後のメッセージはユーザーの発言である必要があります。");
            }

            // メインの生成処理をLLMに委譲        
            await _llm.GenerateResponseAsync(
                prompt: userPrompt,
                systemMessage: systemMessage,
                attachmentData: null, // File.ReadAllBytes("assets/attachment.png"),
                attachmentMediaType: string.Empty, // "image/png",
                tools: await _tool.GetToolsAsync(),
                onProgress: (llmResponse) =>
                {
                    var contextItem = new ContextItem(llmResponse.Role, llmResponse.Content);
                    MyLog.LogWrite($"Generating response: {llmResponse.Content}");
                    onProgress();
                },
                onComplete: async (llmResponse) =>
                {
                    var contextItem = new ContextItem(llmResponse.Role, llmResponse.Content.Trim());
                    await AddContextItemAsync(contextItem);
                    MyLog.LogWrite($"Generated response added to context: {llmResponse.Content}");
                    onComplete();
                }
            );

            // ファクト抽出処理を実施
            await _llm.GenerateResponseAsync(
                prompt: userPrompt,
                systemMessage: "ユーザーの入力から、ファクトを抽出してください。\n\n" +
                               "抽出したファクトは、以下のJSON形式で出力してください。\n" +
                               "{\"key\": \"value\"}\n\n" +
                               "複数のファクトを1つのJSONオブジェクトにまとめて出力してください。\n" +
                               "もし抽出すべきファクトがない場合は、{}とだけ出力してください。\n\n" +
                               "# 今までのファクト\n" +
                               string.Join("\n", facts.Select(kvp => $"{kvp.Key}: {kvp.Value}")),
                attachmentData: null,
                attachmentMediaType: string.Empty,
                tools: [],
                onProgress: (llmResponse) =>
                { },
                onComplete: async (llmResponse) =>
                {
                    if (llmResponse.Role != Common.Role.Assistant)
                    {
                        return;
                    }
                    // 抽出結果をパースしてファクト辞書に追加
                    try
                    {
                        var json = llmResponse.Content.Trim();
                        var parsedFacts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (parsedFacts != null)
                        {
                            foreach (var kvp in parsedFacts)
                            {
                                facts[kvp.Key] = kvp.Value;
                                MyLog.LogWrite($"Extracted fact: {kvp.Key} = {kvp.Value}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MyLog.LogWrite($"Failed to parse extracted facts: {ex.Message}");
                    }
                }
            );

            // 自己採点処理を実施
            await _llm.GenerateResponseAsync(
                prompt: "<system>会話履歴を参照し、ユーザーの最新の質問に対するあなたの回答が適切であったかを0から100のスコアで評価してください。返答はスコアのみとしてください。</system>\n",
                systemMessage: await GetSystemMessageAsync(),
                attachmentData: null,
                attachmentMediaType: string.Empty,
                tools: [],
                onProgress: (llmResponse) =>
                { },
                onComplete: async (llmResponse) =>
                {
                    if (llmResponse.Role != Common.Role.Assistant)
                    {
                        return;
                    }
                    // 抽出結果をデバッグ情報に挿入
                    MyLog.SetDebugInfo("Self-Evaluation Score", llmResponse.Content.Trim());
                }
            );

        }

        public void Dispose()
        {
            
        }
    }
}