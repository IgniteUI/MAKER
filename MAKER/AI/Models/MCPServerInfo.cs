namespace MAKER.AI.Models
{
    public class MCPServerInfo
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public Uri Url { get; set; }

        public string? ApiKey { get; set; }
    }
}
