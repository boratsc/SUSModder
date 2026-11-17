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
        public string? PackCode { get; set; }
        public string? PackModName { get; set; }
        public string? FullModVersion { get; set; }
        public bool Installable { get; set; }
        public string? VtStatus { get; set; }
    }
}
