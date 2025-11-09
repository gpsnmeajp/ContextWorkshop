using System;
using System.Text.Json.Serialization;

namespace ContextWorkshop.Interface
{
    public static class Common
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum Role
        {
            System,
            User,
            Assistant,
            Tool,
            Unknown
        }

    }
}