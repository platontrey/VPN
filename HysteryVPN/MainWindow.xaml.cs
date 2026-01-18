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

namespace HysteryVPN
{
    public partial class MainWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private readonly SettingsManager _settingsManager;
        private readonly Logger _logger;
        private readonly ConfigGenerator _configGenerator;
        private readonly RouteManager _routeManager;
        private readonly VpnManager _vpnManager;
        private double _currentLat = 0, _currentLon = 0;
        private bool _hasLocation = false;
        private bool _isDragging = false;
        private Point _lastMousePosition;
        private Ellipse? _userMarker;
        private System.Windows.Shapes.Path _mapPath = new();
        private GeoJsonFeatureCollection? _geoJsonData;
        private Size _lastMapSize = Size.Empty;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.SizeChanged += MainWindow_SizeChanged;

            _logger = new Logger(LogText, Dispatcher);
            _settingsManager = new SettingsManager(_logger);
            _configGenerator = new ConfigGenerator(_logger);
            _routeManager = new RouteManager(_logger);
            _vpnManager = new VpnManager(_logger, _configGenerator, _routeManager);
            
                        StatusText.Text = "Not connected";
                        ConnectionText.Text = "---";
                        IpText.Text = "Unprotected";
            
                        // Загрузка сохраненной ссылки
           string savedLink = _settingsManager.GetSetting<string>("SavedLink", "");
           _logger.Log($"Loaded saved link: '{savedLink}'");
           if (!string.IsNullOrEmpty(savedLink))
           {
               LinkInput.Text = savedLink;
               _logger.Log($"Set LinkInput.Text to: '{LinkInput.Text}'");
           }

            // Загрузка состояния слайдера TURN
            EnableTurnToggle.Value = _settingsManager.GetSetting<bool>("EnableTurn", false) ? 1 : 0;

            // Загрузка состояния слайдера Zapret
            EnableZapretToggle.Value = _settingsManager.GetSetting<bool>("EnableZapret", false) ? 1 : 0;

            LinkInput.TextChanged += LinkInput_TextChanged;
            EnableTurnToggle.ValueChanged += EnableTurnToggle_ValueChanged;
            EnableZapretToggle.ValueChanged += EnableZapretToggle_ValueChanged;

            _logger.Log("Ready. Paste your hy2:// link and press CONNECT.");
            _logger.Log("Logs are copyable (Ctrl+C).");
        }

        private void LinkInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string link = LinkInput.Text.Trim();
            _logger.Log($"Saving link: '{link}'");
            _settingsManager.SetSetting("SavedLink", link);
        }

        private void EnableTurnToggle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _settingsManager.SetSetting("EnableTurn", EnableTurnToggle.Value == 1);
        }

        private void EnableZapretToggle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _settingsManager.SetSetting("EnableZapret", EnableZapretToggle.Value == 1);
        }

        private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                // Для toggle switch: переключить между 0 и 1
                double newValue = slider.Value == 0 ? 1 : 0;

                // Анимировать Value для плавного перемещения
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = newValue,
                    Duration = TimeSpan.FromSeconds(0.3),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                slider.BeginAnimation(Slider.ValueProperty, animation);

                e.Handled = true; // Предотвратить стандартное мгновенное изменение
            }
        }

        private async void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_vpnManager.IsConnected)
            {
                _vpnManager.StopVpn();
                await UpdateUI();
            }
            else
            {
                string link = LinkInput.Text.Trim();
                bool enableTurn = EnableTurnToggle.Value == 1;
                bool enableZapret = EnableZapretToggle.Value == 1;

                // Show progress and disable controls
                ConnectionProgressPanel.Visibility = Visibility.Visible;
                ActionBtn.IsEnabled = false;
                LinkInput.IsEnabled = false;
                EnableTurnToggle.IsEnabled = false;
                EnableZapretToggle.IsEnabled = false;

                try
                {
                    await _vpnManager.StartVpnAsync(link, enableTurn, enableZapret);
                    await UpdateUI();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to start VPN: {ex.Message}");
                    await UpdateUI();
                }
                finally
                {
                    // Hide progress and re-enable controls
                    ConnectionProgressPanel.Visibility = Visibility.Collapsed;
                    ActionBtn.IsEnabled = true;
                    LinkInput.IsEnabled = true;
                    EnableTurnToggle.IsEnabled = true;
                    EnableZapretToggle.IsEnabled = true;
                }
            }
        }

        private async Task UpdateUI()
        {
            StatusText.Text = _vpnManager.IsConnected ? "Connected" : "Not connected";
            ProtectionStatusText.Text = _vpnManager.IsConnected ? "You are protected" : "You are unprotected";
            ConnectionText.Text = _vpnManager.IsConnected ? "Active" : "---";
            IpText.Text = _vpnManager.IsConnected ? MaskIp(_vpnManager.ServerIp) : "Unprotected";
            if (_vpnManager.IsConnected)
            {
                var countryCode = await GetCountryCodeAsync(_vpnManager.ServerIp);
                await LoadFlagAsync(countryCode);
            }
            else
            {
                CountryFlag.Visibility = Visibility.Collapsed;
            }
            if (_vpnManager.IsConnected)
            {
                ActionBtn.Content = "DISCONNECT";
                ActionBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60)); // Red
                LinkInput.IsEnabled = false;
                EnableTurnToggle.IsEnabled = false;
                EnableZapretToggle.IsEnabled = false;
            }
            else
            {
                ActionBtn.Content = "CONNECT";
                ActionBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)); // Green
                LinkInput.IsEnabled = true;
                EnableTurnToggle.IsEnabled = true;
                EnableZapretToggle.IsEnabled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _vpnManager.StopVpn();
            base.OnClosed(e);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsSystemThemeDark())
            {
                EnableDarkModeForWindow();
            }
            await LoadGeoJsonAsync();
            await UpdateUI();
            await GetUserLocationAsync();
            _logger.Log("MainWindow loaded and user location fetched");
        }

        private void EnableDarkModeForWindow()
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int useDarkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
        }

        private bool IsSystemThemeDark()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object? value = key.GetValue("AppsUseLightTheme");
                        if (value != null)
                        {
                            return (int)value == 0;
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return true; // Default to dark
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
                CountryFlag.Visibility = Visibility.Collapsed;
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
                CountryFlag.Source = bitmap;
                CountryFlag.Visibility = Visibility.Visible;
            }
            catch
            {
                CountryFlag.Visibility = Visibility.Collapsed;
            }
        }

        private async Task GetUserLocationAsync()
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetStringAsync("http://ip-api.com/json/");
                _logger.Log($"API response: {response}");
                var json = JsonDocument.Parse(response);
                if (json.RootElement.TryGetProperty("lat", out var latProp) &&
                    json.RootElement.TryGetProperty("lon", out var lonProp))
                {
                    _currentLat = latProp.GetDouble();
                    _currentLon = lonProp.GetDouble();
                    _hasLocation = true;
                    _logger.Log($"User location: lat {_currentLat}, lon {_currentLon}");
                    await Dispatcher.InvokeAsync(() => UpdateUserPosition(_currentLat, _currentLon));
                }
                else
                {
                    _logger.Log("Failed to parse location from API response");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to get user location: {ex.Message}");
            }
        }

        private void UpdateUserPosition(double lat, double lon)
        {
            if (MapContainer.ActualWidth == 0 || MapContainer.ActualHeight == 0)
                return;

            Point p = LatLonToPoint(lat, lon);

            if (_userMarker == null)
            {
                _userMarker = new Ellipse
                {
                    Width = 15,
                    Height = 15,
                    Fill = Brushes.Red,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    IsHitTestVisible = false
                };

                // Эффект свечения (Glow)
                var glowEffect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 20,
                    Color = Colors.Red,
                    Opacity = 0.8,
                    ShadowDepth = 0
                };
                _userMarker.Effect = glowEffect;

                // Анимация пульсации
                var scale = new ScaleTransform(1, 1);
                _userMarker.RenderTransform = scale;
                _userMarker.RenderTransformOrigin = new Point(0.5, 0.5);

                var anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1,
                    To = 1.6,
                    Duration = TimeSpan.FromSeconds(1.5),
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    AutoReverse = true
                };

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);

                MapCanvas.Children.Add(_userMarker);
            }

            Canvas.SetLeft(_userMarker, p.X - 7.5);
            Canvas.SetTop(_userMarker, p.Y - 7.5);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_hasLocation)
            {
                UpdateUserPosition(_currentLat, _currentLon);
            }
        }

        private void MapContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_geoJsonData != null)
            {
                DrawMap();
            }
            if (_hasLocation)
            {
                UpdateUserPosition(_currentLat, _currentLon);
            }
        }

        private void MapRoot_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoom = e.Delta > 0 ? 1.1 : 0.9;
            double newScale = MapScaleTransform.ScaleX * zoom;

            if (newScale < 0.5 || newScale > 5)
                return;

            Point mouse = e.GetPosition(MapRoot);

            MapTranslateTransform.X =
                mouse.X - zoom * (mouse.X - MapTranslateTransform.X);
            MapTranslateTransform.Y =
                mouse.Y - zoom * (mouse.Y - MapTranslateTransform.Y);

            MapScaleTransform.ScaleX = newScale;
            MapScaleTransform.ScaleY = newScale;
        }


        private Point LatLonToPoint(double lat, double lon)
        {
            double canvasWidth = MapContainer.ActualWidth;
            double canvasHeight = MapContainer.ActualHeight;

            // Clamp latitude to avoid infinity
            lat = Math.Max(-85, Math.Min(85, lat));

            // Mercator projection
            double x = (lon + 180) / 360 * canvasWidth;
            double latRad = lat * Math.PI / 180;
            double mercY = Math.Log(Math.Tan(Math.PI / 4 + latRad / 2));
            double normalizedY = mercY / Math.PI; // -1 to 1
            double y = (1 - normalizedY) * canvasHeight / 2;

            // Ensure finite values
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                return new Point(0, 0); // Fallback
            }

            return new Point(x, y);
        }

        private void MapRoot_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _isDragging = true;
                _lastMousePosition = e.GetPosition(MapRoot);
                MapRoot.CaptureMouse();
            }
        }

        private void MapRoot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPosition = e.GetPosition(MapRoot);
                double deltaX = currentPosition.X - _lastMousePosition.X;
                double deltaY = currentPosition.Y - _lastMousePosition.Y;

                MapTranslateTransform.X += deltaX;
                MapTranslateTransform.Y += deltaY;

                _lastMousePosition = currentPosition;
            }
        }

        private void MapRoot_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _isDragging = false;
                MapRoot.ReleaseMouseCapture();
            }
        }

        private async Task LoadGeoJsonAsync()
        {
            try
            {
                string json = await File.ReadAllTextAsync("countries.geojson");
                _geoJsonData = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(json);
                DrawMap();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load GeoJSON: {ex.Message}");
            }
        }

        private void DrawMap()
        {
            if (_geoJsonData == null) return;

            Size currentSize = new Size(MapContainer.ActualWidth, MapContainer.ActualHeight);
            if (_lastMapSize != Size.Empty && Math.Abs(_lastMapSize.Width - currentSize.Width) < 50 && Math.Abs(_lastMapSize.Height - currentSize.Height) < 50)
                return; // Не перерисовывать если изменение размера мало

            _lastMapSize = currentSize;

            // Clear old paths
            VectorMapCanvas.Children.Clear();

            double canvasWidth = currentSize.Width;
            double canvasHeight = currentSize.Height;

            foreach (var feature in _geoJsonData.Features)
            {
                var geometry = CreateCountryGeometry(feature, canvasWidth, canvasHeight);
                if (geometry != null)
                {
                    var path = new System.Windows.Shapes.Path
                    {
                        Data = geometry,
                        Fill = null, // Убрать заливку для оптимизации
                        Stroke = Brushes.DarkGray,
                        StrokeThickness = 0.2,
                        IsHitTestVisible = false // Disable hit testing for performance
                    };
                    VectorMapCanvas.Children.Add(path);
                }
            }
        }

        private Geometry? CreateCountryGeometry(GeoJsonFeature feature, double canvasWidth, double canvasHeight)
        {
            if (feature.Geometry.Type != "MultiPolygon" && feature.Geometry.Type != "Polygon") return null;

            var pathGeometry = new PathGeometry();
            var pathFigureCollection = new PathFigureCollection();

            try
            {
                if (feature.Geometry.Type == "Polygon")
                {
                    var rings = feature.Geometry.Coordinates.Deserialize<List<List<List<double>>>>();
                    if (rings != null)
                    {
                        foreach (var ring in rings)
                        {
                            var pathFigure = CreatePathFigure(ring, canvasWidth, canvasHeight);
                            if (pathFigure != null)
                            {
                                pathFigureCollection.Add(pathFigure);
                            }
                        }
                    }
                }
                else if (feature.Geometry.Type == "MultiPolygon")
                {
                    var polygons = feature.Geometry.Coordinates.Deserialize<List<List<List<List<double>>>>>();
                    if (polygons != null)
                    {
                        foreach (var polygon in polygons)
                        {
                            foreach (var ring in polygon)
                            {
                                var pathFigure = CreatePathFigure(ring, canvasWidth, canvasHeight);
                                if (pathFigure != null)
                                {
                                    pathFigureCollection.Add(pathFigure);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing coordinates for {feature.Properties.Name}: {ex.Message}");
                return null;
            }

            if (pathFigureCollection.Count > 0)
            {
                pathGeometry.Figures = pathFigureCollection;
                return pathGeometry;
            }

            return null;
        }

        private PathFigure? CreatePathFigure(List<List<double>> ring, double canvasWidth, double canvasHeight)
        {
            if (ring.Count == 0) return null;

            var pathSegments = new PathSegmentCollection();
            bool isFirst = true;
            Point startPoint = new Point();
            Point lastPoint = new Point();

            int step = 10; // Упрощение: брать каждую 10-ую точку для уменьшения количества линий
            for (int i = 0; i < ring.Count; i += step)
            {
                var coord = ring[i];
                double lon = coord[0];
                double lat = coord[1];

                Point point = LatLonToPoint(lat, lon);

                if (isFirst)
                {
                    startPoint = point;
                    lastPoint = point;
                    isFirst = false;
                }
                else
                {
                    pathSegments.Add(new LineSegment(point, true));
                    lastPoint = point;
                }
            }

            // Добавить последнюю точку, если не добавлена
            if (ring.Count > 0 && (ring.Count - 1) % step != 0)
            {
                var coord = ring[ring.Count - 1];
                double lon = coord[0];
                double lat = coord[1];
                Point point = LatLonToPoint(lat, lon);
                if (point != lastPoint)
                {
                    pathSegments.Add(new LineSegment(point, true));
                }
            }

            return new PathFigure(startPoint, pathSegments, true);
        }


    }
}
