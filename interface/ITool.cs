using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using ContextWorkshop.Interface;

namespace ContextWorkshop.Interface
{
    public interface ITool
    {
        Task InitializeAsync();
        Task ResetAsync();
        Task<IList<AITool>> GetToolsAsync();
    }
}