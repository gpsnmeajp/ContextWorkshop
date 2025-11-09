using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ContextWorkshop.Interface;

namespace ContextWorkshop
{
    public class Ui : IUi, IDisposable
    {
        private readonly IContext _context;
        public Ui(IContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            
        }

        public async Task InitializeAsync()
        {

        }

        public async Task ResetAsync()
        {

        }

        public async Task RunMainLoopAsync()
        {
            while (true)
            {
                AnsiConsole.Clear();

                // システムプロンプト表示
                var panel = new Panel(Markup.Escape(await _context.GetSystemMessageAsync()));
                panel.Header = new PanelHeader("System Prompt ( " + await _context.GetSystemMessageTokenCountAsync() + " Tokens )");
                AnsiConsole.Write(panel);

                // 会話履歴表示
                var contextItems = await _context.GetContextItemsAsync();
                var table = new Table();
                table.Caption = new TableTitle("Conversation History");
                table.AddColumn("ID");
                table.AddColumn("Role");
                table.AddColumn("Content");
                table.AddColumn("Timestamp");

                foreach (var item in contextItems)
                {
                    table.AddRow(item.Id.ToString(), item.Role.ToString(), Markup.Escape(item.Content), item.Timestamp.ToLocalTime().ToString());
                }
                AnsiConsole.Write(table);

                // デバッグ情報表示
                var debugTable = new Table();
                debugTable.Caption = new TableTitle("Debug Info");
                debugTable.AddColumn("key");
                debugTable.AddColumn("value");
                var debugInfo = MyLog.GetDebugInfo();
                foreach (var kvp in debugInfo)
                {
                    debugTable.AddRow(Markup.Escape(kvp.Key), Markup.Escape(kvp.Value));
                }
                AnsiConsole.Write(debugTable);

                // ユーザープロンプト入力
                var prompt = await AnsiConsole.PromptAsync(
                    new TextPrompt<string>("prompt: "));

                if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                
                var newItem = new ContextItem(Common.Role.User, prompt);
                await _context.AddContextItemAsync(newItem);
                await _context.GenerateAsync(
                    onProgress: () =>
                    {
                        AnsiConsole.MarkupLine($"[grey]Generating response...[/]");
                    },
                    onComplete: async () =>
                    {
                        AnsiConsole.MarkupLine($"[green]Response generation complete.[/]");
                    });
            }
        }
    }
}