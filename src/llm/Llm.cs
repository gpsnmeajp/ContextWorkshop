using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI;
using System.Runtime.CompilerServices;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging;
using ContextWorkshop.Interface;
using System.Numerics;
using OpenAI.Responses;

namespace ContextWorkshop
{
    public class Llm : ILlm, IDisposable
    {
        const string endpoint = "https://openrouter.ai/api/v1/";
        const string model = "google/gemini-2.5-flash";
        string apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "-";
        TimeSpan timeout = TimeSpan.FromSeconds(180);
        const float temperature = 0.7f;
        const int maxTokens = 8192;

        string? responseText = null;

        public Llm()
        {
        }

        public async Task InitializeAsync()
        {
        }

        public async Task ResetAsync()
        {
        }

        public async Task GenerateResponseAsync(string prompt, string systemMessage, ReadOnlyMemory<byte>? attachmentData, string attachmentMediaType, IList<AITool> tools, Action<LlmResponse> onProgress, Action<LlmResponse> onComplete)
        {
            MyLog.LogWrite($"LLMモデル: {model}");

            // カスタムHttpHandlerを作成
            var httpHandler = new OpenRouterHttpHandler();
            var openAIClientOptions = new OpenAIClientOptions()
            {
                Endpoint = new Uri(endpoint),
                Transport = new HttpClientPipelineTransport(
                    new HttpClient(httpHandler)
                    {
                        Timeout = timeout,
                    }
                ),
            };
            MyLog.LogWrite($"LLMエンドポイント: {openAIClientOptions.Endpoint}");

            var chatClient = new OpenAI.Chat.ChatClient(
                model: model ?? "-",
                new ApiKeyCredential(apiKey),
                openAIClientOptions
            );
            MyLog.LogWrite($"APIキーの長さ: {apiKey.Length}文字");

            // ツール呼び出しの構成
            var client = ChatClientBuilderChatClientExtensions
                .AsBuilder(chatClient.AsIChatClient())
                .UseFunctionInvocation(configure: options =>
                {
                    options.MaximumIterationsPerRequest = 30;
                    options.AllowConcurrentInvocation = false;
                    options.FunctionInvoker = (context, cancellationToken) =>
                    {
                        return MyFunctionInvoker(context, cancellationToken, onProgress, onComplete);
                    };
                }
                )
                .Build();

            MyLog.LogWrite($"システムプロンプト: {systemMessage.Length}文字");

            // ユーザー入力を構築
            List<AIContent> userContents = new List<AIContent>() { new TextContent(prompt) };

            // 画像添付があるならつける(とりあえず1つだけ対応)
            if (attachmentData.HasValue && attachmentData.Value.Length > 0 && !string.IsNullOrEmpty(attachmentMediaType))
            {
                userContents.Add(new DataContent(attachmentData.Value, attachmentMediaType));
                MyLog.LogWrite($"添付ファイル付き: {attachmentMediaType}, サイズ: {attachmentData.Value.Length} バイト");
            }

            // チャットメッセージを構築
            List<ChatMessage> chatMessages = new List<ChatMessage>()
            {
                new ChatMessage(ChatRole.System, systemMessage),
                new ChatMessage(ChatRole.User, userContents)
            };

            MyLog.LogWrite($"生成開始...");

            // ストリーミングで応答を取得開始
            List<ChatResponseUpdate> updates = [];
            responseText = "";

            // ストリーミングで応答を取得
            await foreach (ChatResponseUpdate update in
                client.GetStreamingResponseAsync(chatMessages, new ChatOptions()
                {
                    Temperature = temperature,
                    ToolMode = ChatToolMode.Auto,
                    Tools = tools,
                    MaxOutputTokens = maxTokens
                }))
            {
                responseText += update.Text;
                updates.Add(update);
                onProgress(new LlmResponse
                (
                    Common.Role.System,
                    "...",
                    false
                ));
            }
            ChatResponse response = updates.ToChatResponse();
            onComplete(new LlmResponse
            (
                Common.Role.Assistant,
                responseText ?? "",
                true
            ));


            var statusCode = httpHandler.lastStatusCode;
            MyLog.LogWrite($"HTTPステータスコード: {statusCode}");
            MyLog.LogWrite($"[生成完了] {response?.Text}");

            var usc = response?.Usage;
            if (usc != null)
            {
                MyLog.LogWrite($"トークン使用量: 入力 {usc.InputTokenCount} トークン, 出力 {usc.OutputTokenCount} トークン, 合計 {usc.TotalTokenCount} トークン");
            }
        }

        // ツール呼び出しの実装
        private async ValueTask<object?> MyFunctionInvoker(FunctionInvocationContext context, CancellationToken cancellationToken, Action<LlmResponse> onProgress, Action<LlmResponse> onComplete)
        {
            var argJson = Serializer.JsonSerialize(new Dictionary<string, object?> { { "call", context.Arguments } }, false);
            MyLog.LogWrite($"関数呼び出し: {context.Function.Name}({argJson}) を実行中...");

            // 関数呼出しされた時点でアシスタントの現時点までの発言は確定させる
            if (!string.IsNullOrEmpty(responseText))
            {
                onComplete(new LlmResponse
                (
                    Common.Role.Assistant,
                    responseText ?? "",
                    false
                ));
            }
            responseText = "";

            // 関数呼出し情報を一時的にトーク履歴に追加する
            onProgress(new LlmResponse
            (
                Common.Role.Tool,
                $"{context.Function.Name}({argJson})",
                false
            ));

            await Task.Delay(500); // 少し待つ

            object? result;
            try
            {
                // 関数を実行
                result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);
                var resultJson = Serializer.JsonSerialize(new Dictionary<string, object?> { { "call", context.Arguments }, { "result", result } }, false);
                MyLog.LogWrite($"関数呼び出し結果: {resultJson}");

                // 関数呼出し結果に差し替える
                onComplete(new LlmResponse
                (
                    Common.Role.Tool,
                    $"{context.Function.Name}({resultJson})",
                    false
                ));

                await Task.Delay(500); // 少し待つ
            }
            catch (Exception ex)
            {
                MyLog.LogWrite($"関数呼び出しエラー: {ex.Message} {ex.StackTrace}");
                var resultJson = Serializer.JsonSerialize(new Dictionary<string, object?> { { "call", context.Arguments }, { "error", ex.Message } }, false);

                // 関数呼出し結果に差し替える
                onComplete(new LlmResponse
                (
                    Common.Role.Tool,
                    $"{context.Function.Name}({resultJson}) -! exception: {ex.Message}",
                    false
                ));
                throw;
            }
            return result;
        }

        public void Dispose()
        {
            
        }
    }
}