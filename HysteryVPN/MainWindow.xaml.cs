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

        // Для плавных движений 2D карты
        private double velocityX = 0.0;
        private double velocityY = 0.0;
        private double targetTranslateX = 0.0;
        private double targetTranslateY = 0.0;
        private double targetScaleX = 1.0;
        private double targetScaleY = 1.0;
        private const double Damping2D = 0.95; // Инерция для 2D карты
        private const double SmoothFactor2D = 0.5; // Плавность следования для 2D карты
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

            // Загрузка состояния чекбокса TURN
             EnableTurnToggle.IsChecked = _settingsManager.GetSetting<bool>("EnableTurn", false);

             // Загрузка состояния чекбокса Zapret
             EnableZapretToggle.IsChecked = _settingsManager.GetSetting<bool>("EnableZapret", false);

             LinkInput.TextChanged += LinkInput_TextChanged;
             EnableTurnToggle.Checked += EnableTurnToggle_Checked;
             EnableTurnToggle.Unchecked += EnableTurnToggle_Unchecked;
             EnableZapretToggle.Checked += EnableZapretToggle_Checked;
             EnableZapretToggle.Unchecked += EnableZapretToggle_Unchecked;

            _logger.Log("Ready. Paste your hy2:// link and press CONNECT.");
            _logger.Log("Logs are copyable (Ctrl+C).");
        }

        private void LinkInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string link = LinkInput.Text.Trim();
            _logger.Log($"Saving link: '{link}'");
            _settingsManager.SetSetting("SavedLink", link);
        }

        private void EnableTurnToggle_Checked(object sender, RoutedEventArgs e)
        {
            _settingsManager.SetSetting("EnableTurn", true);
        }

        private void EnableTurnToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _settingsManager.SetSetting("EnableTurn", false);
        }

        private void EnableZapretToggle_Checked(object sender, RoutedEventArgs e)
        {
            _settingsManager.SetSetting("EnableZapret", true);
        }

        private void EnableZapretToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _settingsManager.SetSetting("EnableZapret", false);
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
                bool enableTurn = EnableTurnToggle.IsChecked ?? false;
                bool enableZapret = EnableZapretToggle.IsChecked ?? false;

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
            CompositionTarget.Rendering -= OnRendering2D;
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

            // Инициализация целей для плавных движений
            targetTranslateX = MapTranslateTransform.X;
            targetTranslateY = MapTranslateTransform.Y;
            targetScaleX = MapScaleTransform.ScaleX;
            targetScaleY = MapScaleTransform.ScaleY;

            // Центрирование камеры на местоположении пользователя с приближением (отложено)
            if (_hasLocation)
            {
                Dispatcher.InvokeAsync(() => CenterOnUserLocation());
            }

            // Подписка на рендеринг для плавных движений 2D карты (по умолчанию 2D)
            CompositionTarget.Rendering += OnRendering2D;

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
                    Width = 4,
                    Height = 4,
                    Fill = Brushes.Red,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
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

            Canvas.SetLeft(_userMarker, p.X - 2);
            Canvas.SetTop(_userMarker, p.Y - 2);
        }

        private void CenterOnUserLocation()
        {
            if (!_hasLocation || MapContainer.ActualWidth == 0 || MapContainer.ActualHeight == 0) return;

            Point userPoint = LatLonToPoint(_currentLat, _currentLon);
            double canvasWidth = MapContainer.ActualWidth;
            double canvasHeight = MapContainer.ActualHeight;
            targetTranslateX = -userPoint.X * 6.0 + canvasWidth / 2;
            targetTranslateY = -userPoint.Y * 6.0 + canvasHeight / 2;
            targetScaleX = 6.0;
            targetScaleY = 6.0;
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
            double newScale = targetScaleX * zoom;

            if (newScale < 0.5 || newScale > 20)
                return;

            Point mouse = e.GetPosition(MapRoot);

            // Скорректировать translate для зума вокруг курсора
            targetTranslateX = mouse.X - zoom * (mouse.X - targetTranslateX);
            targetTranslateY = mouse.Y - zoom * (mouse.Y - targetTranslateY);

            targetScaleX = newScale;
            targetScaleY = newScale;
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

                // Накопление скорости для инерции, с учетом масштаба
                velocityX = deltaX * targetScaleX;
                velocityY = deltaY * targetScaleY;

                // Обновление цели
                targetTranslateX += velocityX;
                targetTranslateY += velocityY;

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

        private void OnRendering2D(object sender, EventArgs e)
        {
            // Если мышь отпущена, продолжаем двигаться по инерции
            if (!_isDragging)
            {
                targetTranslateX += velocityX;
                targetTranslateY += velocityY;

                // Затухание скорости
                velocityX *= Damping2D;
                velocityY *= Damping2D;

                // Остановить, если скорость слишком мала
                if (Math.Abs(velocityX) < 0.01) velocityX = 0;
                if (Math.Abs(velocityY) < 0.01) velocityY = 0;
            }

            // Плавная интерполяция к целевой позиции и масштабу
            MapTranslateTransform.X += (targetTranslateX - MapTranslateTransform.X) * SmoothFactor2D;
            MapTranslateTransform.Y += (targetTranslateY - MapTranslateTransform.Y) * SmoothFactor2D;
            MapScaleTransform.ScaleX += (targetScaleX - MapScaleTransform.ScaleX) * SmoothFactor2D;
            MapScaleTransform.ScaleY += (targetScaleY - MapScaleTransform.ScaleY) * SmoothFactor2D;
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

            int step = Math.Max(1, ring.Count / 5000); // Адаптивный шаг для одинакового отображения на разных разрешениях
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
                    pathSegments.Add(new System.Windows.Media.LineSegment(point, true));
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
                    pathSegments.Add(new System.Windows.Media.LineSegment(point, true));
                }
            }

            return new PathFigure(startPoint, pathSegments, true);
        }

        private void MapTypeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MapRoot == null || GlobeContainer == null) return;

            ComboBoxItem item = MapTypeSelector.SelectedItem as ComboBoxItem;
            if (item != null)
            {
                if (item.Content.ToString() == "3D Globe")
                {
                    MapRoot.Visibility = Visibility.Collapsed;
                    GlobeContainer.Visibility = Visibility.Visible;
                    GlobeControls.Visibility = Visibility.Visible;

                    // Отписка от рендеринга 2D карты
                    CompositionTarget.Rendering -= OnRendering2D;
                }
                else
                {
                    MapRoot.Visibility = Visibility.Visible;
                    GlobeContainer.Visibility = Visibility.Collapsed;
                    GlobeControls.Visibility = Visibility.Collapsed;

                    // Подписка на рендеринг для плавных движений 2D карты
                    CompositionTarget.Rendering -= OnRendering2D;
                    CompositionTarget.Rendering += OnRendering2D;
                }
            }
        }

        private void ToggleAtmosphere_Checked(object sender, RoutedEventArgs e)
        {
            if (GlobeContainer is MapboxStyleGlobe globe)
            {
                var checkbox = sender as CheckBox;
                globe.SetAtmosphereVisible(checkbox?.IsChecked ?? true);
            }
        }

        private void ToggleStars_Checked(object sender, RoutedEventArgs e)
        {
            if (GlobeContainer is MapboxStyleGlobe globe)
            {
                var checkbox = sender as CheckBox;
                globe.SetStarsVisible(checkbox?.IsChecked ?? true);
            }
        }

        private void RotationSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GlobeContainer is MapboxStyleGlobe globe)
            {
                globe.SetRotationSpeed(e.NewValue);
            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (GlobeContainer is MapboxStyleGlobe globe)
            {
                globe.ResetView();
            }
        }
    }
}
