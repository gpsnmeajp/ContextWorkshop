using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using ContextWorkshop.Interface;

namespace ContextWorkshop.Interface
{
    public class ContextItem
    {
        public Guid Id { get; set; } = Guid.Empty;
        public Common.Role Role { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public ContextItem()
        {
            Id = Guid.Empty;
            Role = Common.Role.Unknown;
            Content = string.Empty;
            Timestamp = DateTime.MinValue;
        }

        public ContextItem(Common.Role role, string content)
        {
            Id = Guid.NewGuid();
            Role = role;
            Content = content;
            Timestamp = DateTime.UtcNow;
        }
        public ContextItem(Guid id, Common.Role role, string content, DateTime timestamp)
        {
            Id = id;
            Role = role;
            Content = content;
            Timestamp = timestamp;
        }

        public override string ToString()
        {
            return $"{Id} {Role}: {Content} ({Timestamp})";
        }
    }
    public interface IContext
    {
        Task InitializeAsync();
        Task ResetAsync();

        Task<List<ContextItem>> GetContextItemsAsync();
        Task AddContextItemAsync(ContextItem item);
        Task EditContextItemAsync(ContextItem item);
        Task DeleteContextItemAsync(Guid id);
        Task GenerateAsync(Action onProgress, Action onComplete);
        Task<string> GetSystemMessageAsync();
        Task<int> GetSystemMessageTokenCountAsync();
    }
}