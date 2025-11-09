using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ContextWorkshop.Interface;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using ModelContextProtocol.Client;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO;

namespace ContextWorkshop
{
    public class Tool : ITool, IDisposable, IAsyncDisposable
    {
        private List<McpClient> _mcpClients = new List<McpClient>();
        List<AITool> _mcpTools = new List<AITool>();

        private class McpConfig
        {
            public Dictionary<string, McpServerConfig> McpServers { get; set; } = new Dictionary<string, McpServerConfig>();
        }

        private class McpServerConfig
        {
            public string? Command { get; set; }
            public List<string>? Args { get; set; }
            public Dictionary<string, string?>? Env { get; set; }
            public string? WorkingDirectory { get; set; }
            public string? Url { get; set; }
            public Dictionary<string, string>? Headers { get; set; }
        }

        public async Task InitializeAsync()
        {
            // MCPツールの初期化を非同期で実行
            try
            {
                await InitMcpToolsAsync();

                // MCPツールを追加
                foreach (var client in _mcpClients)
                {
                    // MCPクライアントから利用可能なツールを取得して追加
                    var mcpTools = await client.ListToolsAsync();
                    foreach (var tool in mcpTools)
                    {
                        _mcpTools.Add(tool);
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.LogWrite($"MCPツールの初期化に失敗しました: {ex.Message} {ex.StackTrace}");
            }
        }

        public async Task ResetAsync()
        {
        }

        public async Task<IList<AITool>> GetToolsAsync()
        {
            List<AITool> tools = [AIFunctionFactory.Create(IsGreater)];
            tools.AddRange(_mcpTools);
            return tools;
        }

        [Description("大小比較をします(a > b)")]
        async Task<bool> IsGreater(
        [Description("1番目の値")] double a,
        [Description("2番目の値")] double b)
        {
            return a > b;
        }

        public async Task InitMcpToolsAsync()
        {
            if (!File.Exists("assets/mcp.json"))
            {
                // 空のmcp.jsonを作る
                var emptyConfig = new McpConfig();
                var emptyJson = JsonSerializer.Serialize(emptyConfig, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory("assets");
                await File.WriteAllTextAsync("assets/mcp.json", emptyJson);
                MyLog.LogWrite("空のmcp.jsonファイルを作成しました。");

                return;
            }
            var json = await File.ReadAllTextAsync("assets/mcp.json");
            var config = JsonSerializer.Deserialize<McpConfig>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (config == null || config.McpServers.Count == 0)
            {
                MyLog.LogWrite("mcp.jsonにMCPサーバーの設定が見つかりません。");
                return;
            }

            // 各MCPサーバーの設定に基づいてクライアントを初期化
            foreach (var kvp in config.McpServers)
            {
                McpClient client;
                var serverName = kvp.Key;
                var serverConfig = kvp.Value;

                if (!string.IsNullOrEmpty(serverConfig.Command))
                {
                    // ローカルコマンドベースのMCPサーバー
                    MyLog.LogWrite($"MCPサーバーを起動中(Stdio): {serverName} コマンド={serverConfig.Command}");
                    var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = serverName,
                        Command = serverConfig.Command,
                        Arguments = serverConfig.Args ?? new List<string>(),
                        WorkingDirectory = serverConfig.WorkingDirectory,
                        EnvironmentVariables = serverConfig.Env ?? new Dictionary<string, string?>(),
                        StandardErrorLines = (line) =>
                        {
                            MyLog.LogWrite($"[MCP:{serverName} STDERR] {line}");
                        }
                    }, LoggerFactory.Create(builder => builder.AddProvider(new MyLogProvider())));

                    client = await McpClient.CreateAsync(clientTransport);
                }
                else if (!string.IsNullOrEmpty(serverConfig.Url))
                {
                    // HTTPベースのMCPサーバー
                    MyLog.LogWrite($"MCPサーバーに接続中(HTTP): {serverName} URL={serverConfig.Url}");
                    var clientTransport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Name = serverName,
                        Endpoint = new Uri(serverConfig.Url),
                        AdditionalHeaders = serverConfig.Headers ?? new Dictionary<string, string>(),
                    }, LoggerFactory.Create(builder => builder.AddProvider(new MyLogProvider())));

                    client = await McpClient.CreateAsync(clientTransport);
                }
                else
                {
                    MyLog.LogWrite($"MCPサーバーの設定が不正です: {serverName}");
                    continue;
                }

                _mcpClients.Add(client);

                foreach (var tool in await client.ListToolsAsync())
                {
                    MyLog.LogWrite($"MCPツール: {tool.Name} ({tool.Description})");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _mcpClients)
            {
                await client.DisposeAsync();
            }
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }
}