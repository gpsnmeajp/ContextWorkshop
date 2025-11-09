using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using ContextWorkshop.Interface;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace ContextWorkshop
{
    public class Tool : ITool, IDisposable
    {
        public async Task InitializeAsync()
        {
        }

        public async Task ResetAsync()
        {
        }

        public async Task<IList<AITool>> GetToolsAsync()
        {
            List<AITool> tools = [AIFunctionFactory.Create(IsGreater)];
            return tools;
        }

        public void Dispose()
        {
        }

        [Description("大小比較をします(a > b)")]
        async Task<bool> IsGreater(
        [Description("1番目の値")] double a,
        [Description("2番目の値")] double b)
        {
            return a > b;
        }
    }
}