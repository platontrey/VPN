    using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Linq;
using HelixToolkit.Wpf;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using WinForms = System.Windows.Forms;
using Wpf = System.Windows;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;

using HysteryVPN.Services;
using HysteryVPN.Models;
using HysteryVPN.Rendering;

namespace HysteryVPN.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // Services
        private readonly SettingsManager _settingsManager;
        private readonly Logger _logger;
        private readonly ConfigGenerator _configGenerator;
        private readonly RouteManager _routeManager;
        private readonly VpnManager _vpnManager;

        // UI Properties
        [ObservableProperty]
        private string statusText = "Not connected";

        [ObservableProperty]
        private string protectionStatusText = "You are unprotected";

        [ObservableProperty]
        private string connectionText = "---";

        [ObservableProperty]
        private string ipText = "Unprotected";

        [ObservableProperty]
        private string actionButtonContent = "CONNECT";

        [ObservableProperty]
        private System.Windows.Media.Brush actionButtonBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113));

        [ObservableProperty]
        private bool isConnectionProgressVisible = false;

        [ObservableProperty]
        private string linkInputText = "";

        partial void OnLinkInputTextChanged(string value)
        {
            _settingsManager.SetSetting("SavedLink", value);
        }

        [ObservableProperty]
        private bool enableTurnToggle = false;

        partial void OnEnableTurnToggleChanged(bool value)
        {
            _settingsManager.SetSetting("EnableTurn", value);
        }

        [ObservableProperty]
        private bool enableZapretToggle = false;

        partial void OnEnableZapretToggleChanged(bool value)
        {
            _settingsManager.SetSetting("EnableZapret", value);
        }

        [ObservableProperty]
        private string logText = "";

        [ObservableProperty]
        private Visibility countryFlagVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private ImageSource countryFlagSource;

        // Map properties
        [ObservableProperty]
        private Visibility mapRootVisibility = Visibility.Visible;

        [ObservableProperty]
        private Visibility openGLHostVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility globeControlsVisibility = Visibility.Collapsed;

        // Commands
        public RelayCommand ActionBtnCommand { get; }
        public RelayCommand<string> MapTypeSelectorCommand { get; }

        // Constructor
        public MainViewModel()
        {
            _logger = new Logger(s => LogText += s, System.Windows.Application.Current.Dispatcher);
            _settingsManager = new SettingsManager(_logger);
            _configGenerator = new ConfigGenerator(_logger);
            _routeManager = new RouteManager(_logger);
            _vpnManager = new VpnManager(_logger, _configGenerator, _routeManager);

            ActionBtnCommand = new RelayCommand(async () => await ActionBtnExecute());
            MapTypeSelectorCommand = new RelayCommand<string>(MapTypeSelectorExecute);

            LoadSettings();
            _logger.Log("Ready. Paste your hy2:// link and press CONNECT.");
            _logger.Log("Logs are copyable (Ctrl+C).");
        }

        private void LoadSettings()
        {
            LinkInputText = _settingsManager.GetSetting<string>("SavedLink", "");
            EnableTurnToggle = _settingsManager.GetSetting<bool>("EnableTurn", false);
            EnableZapretToggle = _settingsManager.GetSetting<bool>("EnableZapret", false);
        }

        private async Task ActionBtnExecute()
        {
            if (_vpnManager.IsConnected)
            {
                _vpnManager.StopVpn();
                await UpdateUI();
            }
            else
            {
                IsConnectionProgressVisible = true;
                // Disable controls logic would be handled in View or via properties

                try
                {
                    await _vpnManager.StartVpnAsync(LinkInputText, EnableTurnToggle, EnableZapretToggle);
                    await UpdateUI();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to start VPN: {ex.Message}");
                    await UpdateUI();
                }
                finally
                {
                    IsConnectionProgressVisible = false;
                    // Re-enable controls
                }
            }
        }

        private async Task UpdateUI()
        {
            StatusText = _vpnManager.IsConnected ? "Connected" : "Not connected";
            ProtectionStatusText = _vpnManager.IsConnected ? "You are protected" : "You are unprotected";
            ConnectionText = _vpnManager.IsConnected ? "Active" : "---";
            IpText = _vpnManager.IsConnected ? MaskIp(_vpnManager.ServerIp) : "Unprotected";
            if (_vpnManager.IsConnected)
            {
                var countryCode = await GetCountryCodeAsync(_vpnManager.ServerIp);
                await LoadFlagAsync(countryCode);
            }
            else
            {
                CountryFlagVisibility = Visibility.Collapsed;
            }
            if (_vpnManager.IsConnected)
            {
                ActionButtonContent = "DISCONNECT";
                ActionButtonBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60));
                // Disable inputs
            }
            else
            {
                ActionButtonContent = "CONNECT";
                ActionButtonBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113));
                // Enable inputs
            }
        }

        private string MaskIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return "Unprotected";
            var parts = ip.Split('.');
            if (parts.Length == 4)
            {
                return $"{parts[0]}.{parts[1]}.***.***";
            }
            return ip;
        }

        private async Task<string> GetCountryCodeAsync(string ip)
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetStringAsync($"http://ipapi.co/{ip}/country/");
                return response.Trim().ToLower();
            }
            catch
            {
                return "";
            }
        }

        private async Task LoadFlagAsync(string countryCode)
        {
            if (string.IsNullOrEmpty(countryCode))
            {
                CountryFlagVisibility = Visibility.Collapsed;
                return;
            }
            try
            {
                using var client = new HttpClient();
                var imageBytes = await client.GetByteArrayAsync($"https://flagcdn.com/w40/{countryCode}.png");
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                using (var stream = new System.IO.MemoryStream(imageBytes))
                {
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                }
                CountryFlagSource = bitmap;
                CountryFlagVisibility = Visibility.Visible;
            }
            catch
            {
                CountryFlagVisibility = Visibility.Collapsed;
            }
        }

        private void MapTypeSelectorExecute(string content)
        {
            if (content == "3D Globe")
            {
                MapRootVisibility = Visibility.Collapsed;
                OpenGLHostVisibility = Visibility.Visible;
                GlobeControlsVisibility = Visibility.Visible;
            }
            else
            {
                MapRootVisibility = Visibility.Visible;
                OpenGLHostVisibility = Visibility.Collapsed;
                GlobeControlsVisibility = Visibility.Collapsed;
            }
        }

        // Additional methods for map logic can be added here
    }
}