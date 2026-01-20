using System;
using System.Windows.Controls;
using System.Windows.Threading;
using TextBox = System.Windows.Controls.TextBox;

namespace HysteryVPN.Services
{
    public class Logger
    {
        private readonly TextBox _logTextBox;
        private readonly Dispatcher _dispatcher;

        public Logger(TextBox logTextBox, Dispatcher dispatcher)
        {
            _logTextBox = logTextBox;
            _dispatcher = dispatcher;
        }

        public void Log(string message)
        {
            string logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            _dispatcher.Invoke(() =>
            {
                _logTextBox.AppendText(logMessage);
                _logTextBox.ScrollToEnd();
            });
        }

        public void LogCore(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            // Очистка цветов консоли
            string clean = System.Text.RegularExpressions.Regex.Replace(message, @"\x1B\[[^@-~]*[@-~]", "");
            _dispatcher.Invoke(() =>
            {
                _logTextBox.AppendText(clean + "\n");
                _logTextBox.ScrollToEnd();
            });
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
