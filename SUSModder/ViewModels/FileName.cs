using ReactiveUI;
using System.Reactive;
using Avalonia.Controls;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;

namespace SUSModder.ViewModels
{
    public class EpicErrorDialogViewModel : ViewModelBase
    {
        private readonly Window _window;
        private string _modName;
        private string _logContent;

        public EpicErrorDialogViewModel(string modName, string logContent, Window window)
        {
            _modName = modName;
            _logContent = logContent;
            _window = window;

            CopyLogCommand = ReactiveCommand.CreateFromTask(CopyLogAsync);
            CloseCommand = ReactiveCommand.Create(Close);
        }

        public string ModName
        {
            get => _modName;
            set => this.RaiseAndSetIfChanged(ref _modName, value);
        }

        public string LogContent
        {
            get => _logContent;
            set => this.RaiseAndSetIfChanged(ref _logContent, value);
        }

        public ReactiveCommand<Unit, Unit> CopyLogCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseCommand { get; }

        private async Task CopyLogAsync()
        {
            try
            {
                // Użyj TopLevel zamiast Application.Current.Clipboard
                var topLevel = TopLevel.GetTopLevel(_window);
                var clipboard = topLevel?.Clipboard;

                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(LogContent);
                    System.Diagnostics.Debug.WriteLine("Log skopiowany do schowka");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Nie można uzyskać dostępu do schowka");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd kopiowania do schowka: {ex.Message}");
            }
        }

        private void Close()
        {
            _window.Close();
        }
    }
}
