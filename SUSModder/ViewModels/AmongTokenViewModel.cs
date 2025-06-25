using ReactiveUI;
using SUSModder.Core.Models;

namespace SUSModder.ViewModels
{
    public class AmongTokenViewModel : ViewModelBase
    {
        private bool _isSelected;

        public AmongToken Token { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        // Właściwości przekierowane z AmongToken
        public int Id => Token.Id;
        public string ServerName => Token.ServerName;
        public string Endpoint => Token.Endpoint;
        public string TokenValue => Token.Token;
        public string Secret => Token.Secret;

        public AmongTokenViewModel(AmongToken token)
        {
            Token = token;
        }
    }
}
