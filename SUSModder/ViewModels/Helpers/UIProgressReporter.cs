using System;
using Avalonia.Threading;
using SUSModder.Core.Utilities;

namespace SUSModder.ViewModels.Helpers
{
    /// <summary>
    /// Reporter postępu dla operacji w UI, przekazuje aktualizacje do UI thread
    /// </summary>
    public class UIProgressReporter : IProgressReporter
    {
        private readonly Action<int, string> _progressCallback;

        public UIProgressReporter(Action<int, string> progressCallback)
        {
            _progressCallback = progressCallback;
        }

        public void Report(int percentage, string? message = null)
        {
            var safeMessage = message ?? "Przetwarzanie...";
            Dispatcher.UIThread.InvokeAsync(() => _progressCallback(percentage, safeMessage));
        }
    }
}
