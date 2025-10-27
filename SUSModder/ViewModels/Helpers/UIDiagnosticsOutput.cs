using System;
using Avalonia.Threading;
using SUSModder.Core.Diagnostics;

namespace SUSModder.ViewModels.Helpers
{
    /// <summary>
    /// Wyjście diagnostyczne dla operacji w UI, przekazuje komunikaty do UI thread
    /// </summary>
    public class UIDiagnosticsOutput : IDiagnosticsOutput
    {
        private readonly Action<string> _messageCallback;

        public UIDiagnosticsOutput(Action<string> messageCallback)
        {
            _messageCallback = messageCallback;
        }

        public void Write(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() => _messageCallback(message));
        }
    }
}
