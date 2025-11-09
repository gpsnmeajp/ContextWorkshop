using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using ContextWorkshop.Interface;

namespace ContextWorkshop.Interface
{
    public interface IUi
    {
        Task InitializeAsync();
        Task ResetAsync();
        Task RunMainLoopAsync();
    }
}