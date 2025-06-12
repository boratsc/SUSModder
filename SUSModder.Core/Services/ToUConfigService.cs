using System.Threading.Tasks;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    public class ToUConfigService
    {
        public void SaveLocalConfig()
        {
            // ModConfigHandler.SaveLocalConfig();
            System.Diagnostics.Debug.WriteLine("SaveLocalConfig called");
        }

        public void LoadLocalConfig()
        {
            // ModConfigHandler.LoadLocalConfig();
            System.Diagnostics.Debug.WriteLine("LoadLocalConfig called");
        }

        public async Task SaveServerConfigAsync()
        {
            // await ModConfigHandler.SaveServerConfigAsync();
            System.Diagnostics.Debug.WriteLine("SaveServerConfigAsync called");
            await Task.Delay(100); // Placeholder
        }

        public async Task LoadServerConfigAsync()
        {
            // await ModConfigHandler.LoadServerConfigAsync();
            System.Diagnostics.Debug.WriteLine("LoadServerConfigAsync called");
            await Task.Delay(100); // Placeholder
        }

        public void LoadLocalTxtConfig()
        {
            // ModConfigHandler.LoadLocalTxtConfig();
            System.Diagnostics.Debug.WriteLine("LoadLocalTxtConfig called");
        }

        public void ChangePresetNames()
        {
            // ModConfigHandler.ChangePresetNames();
            System.Diagnostics.Debug.WriteLine("ChangePresetNames called");
        }

        public bool SetLobbySize(int playerCount, out string errorMessage)
        {
            return LobbyUtils.SetLobbyPlayers(playerCount, out errorMessage);
        }
    }
}
