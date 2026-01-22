using System;
using System.Windows.Controls;
using System.Windows.Threading;
using TextBox = System.Windows.Controls.TextBox;

namespace HysteryVPN.Services
{
    public class Logger
    {
        private readonly Action<string> _logAction;
        private readonly Dispatcher _dispatcher;

        public Logger(Action<string> logAction, Dispatcher dispatcher)
        {
            _logAction = logAction;
            _dispatcher = dispatcher;
        }

        public void Log(string message)
        {
            string logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            _dispatcher.Invoke(() => _logAction(logMessage + "\n"));
        }

        public void LogCore(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            // Очистка цветов консоли
            string clean = System.Text.RegularExpressions.Regex.Replace(message, @"\x1B\[[^@-~]*[@-~]", "");
            _dispatcher.Invoke(() => _logAction(clean + "\n"));
        }

        public void LogError(string message)
        {
            Log($"ERROR: {message}");
        }

        public void LogWarning(string message)
        {
            Log($"WARNING: {message}");
        }
    }
}
