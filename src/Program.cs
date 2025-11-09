using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ContextWorkshop
{
    class Program
    {
        static void Main(string[] args)
        {
            var tool = new Tool();   
            var llm = new Llm();
            var context = new Context(llm, tool);
            var ui = new Ui(context);
            Task.Run(async () =>
            {
                // Initialize
                await ui.InitializeAsync();
                await context.InitializeAsync();
                await llm.InitializeAsync();
                await tool.InitializeAsync();

                // Main application loop
                await ui.RunMainLoopAsync();
            }).GetAwaiter().GetResult();
        }
    }
}