namespace SUSModder.Core.Models
{
    public class DiscordServer
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string InviteLink { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? IconPath { get; set; }
        public int MemberCount { get; set; }
    }
}
