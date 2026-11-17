using SUSModder.Core.Models;

namespace SUSModder.Core.Configuration
{
    public static class DiscordServerAdapter
    {
        public static DiscordServer FromServerData(DiscordServerData serverData)
        {
            return new DiscordServer
            {
                Id = serverData.Id,
                Name = serverData.Name,
                InviteLink = serverData.Link,
                Description = serverData.Description,
                IconPath = !string.IsNullOrEmpty(serverData.Icon) ? serverData.Icon : null,
                PackCode = serverData.PackCode,
                PackModName = serverData.PackModName,
                FullModVersion = serverData.FullModVersion,
                Installable = serverData.Installable ?? false,
                VtStatus = serverData.VtStatus
            };
        }

        public static List<DiscordServer> FromServerDataList(List<DiscordServerData> serverDataList)
        {
            return serverDataList.Select(FromServerData).ToList();
        }
    }
}
