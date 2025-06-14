using SUSModder.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SUSModder.Services
{
    public static class ConsoleLogger
    {
        private static bool _isInitialized = false;
        private static DebugTraceListener? _debugListener;
        private static ConsoleTextWriter? _consoleWriter;

        public static void Initialize()
        {
            if (_isInitialized) return;

            // Przechwytuj Debug.WriteLine przez Trace.Listeners
            _debugListener = new DebugTraceListener();
            Trace.Listeners.Add(_debugListener);

            // Przechwytuj Console.WriteLine
            _consoleWriter = new ConsoleTextWriter();
            Console.SetOut(_consoleWriter);
            Console.SetError(_consoleWriter);

            _isInitialized = true;
        }

        public static void Shutdown()
        {
            if (_debugListener != null)
            {
                Trace.Listeners.Remove(_debugListener);
                _debugListener = null;
            }

            _consoleWriter = null;
            _isInitialized = false;
        }

        // Metody pomocnicze (opcjonalne - dla bezpośredniego użycia)
        public static void WriteLine(string message, LogLevel level = LogLevel.Info)
        {
            Debug.WriteLine(message);
            ConsoleWindow.WriteLog(message, level);
        }

        public static void WriteInfo(string message) => WriteLine(message, LogLevel.Info);
        public static void WriteWarning(string message) => WriteLine(message, LogLevel.Warning);
        public static void WriteError(string message) => WriteLine(message, LogLevel.Error);
        public static void WriteDebug(string message) => WriteLine(message, LogLevel.Debug);
    }

    // Przechwytuje Debug.WriteLine
    internal class DebugTraceListener : TraceListener
    {
        public override void Write(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                ConsoleWindow.WriteLog(message, LogLevel.Debug);
            }
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                ConsoleWindow.WriteLog(message, LogLevel.Debug);
            }
        }
    }

    // Przechwytuje Console.WriteLine
    internal class ConsoleTextWriter : TextWriter
    {
        private readonly TextWriter _originalOut;
        private readonly StringBuilder _buffer = new();

        public ConsoleTextWriter()
        {
            _originalOut = Console.Out;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _originalOut.Write(value);

            if (value == '\n')
            {
                var message = _buffer.ToString().TrimEnd('\r', '\n');
                if (!string.IsNullOrEmpty(message))
                {
                    // Określ poziom na podstawie zawartości wiadomości
                    var level = DetermineLogLevel(message);
                    ConsoleWindow.WriteLog(message, level);
                }
                _buffer.Clear();
            }
            else
            {
                _buffer.Append(value);
            }
        }

        public override void WriteLine(string? value)
        {
            _originalOut.WriteLine(value);

            if (!string.IsNullOrEmpty(value))
            {
                var level = DetermineLogLevel(value);
                ConsoleWindow.WriteLog(value, level);
            }
        }

        private static LogLevel DetermineLogLevel(string message)
        {
            var lowerMessage = message.ToLower();

            if (lowerMessage.Contains("error") || lowerMessage.Contains("błąd") ||
                lowerMessage.Contains("exception") || lowerMessage.Contains("wyjątek"))
                return LogLevel.Error;

            if (lowerMessage.Contains("warning") || lowerMessage.Contains("ostrzeżenie") ||
                lowerMessage.Contains("warn"))
                return LogLevel.Warning;

            if (lowerMessage.Contains("[appupdate]") || lowerMessage.Contains("debug"))
                return LogLevel.Debug;

            return LogLevel.Info;
        }
    }
}
