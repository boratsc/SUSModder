namespace SUSModder.Core.Models
{
    public class DiscordServer
    {
        public string Name { get; set; } = string.Empty;
        public string InviteLink { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? IconPath { get; set; }
    }
}