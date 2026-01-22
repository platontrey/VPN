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
using HysteryVPN.ViewModels;

namespace HysteryVPN
{
    /// <summary>
    /// Уровень качества рендеринга.
    /// </summary>
    public enum QualityLevel
    {
        Low = 32,
        Medium = 48,
        High = 64,
        Ultra = 96
    }
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int pt_x;
            public int pt_y;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;

        private HookProc _mouseHookProc;
        private IntPtr _mouseHookHandle = IntPtr.Zero;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private MainViewModel _viewModel;
        private double _currentLat = 0, _currentLon = 0;
        private bool _hasLocation = false;
        private bool _isDragging = false;
        private Point _lastMousePosition;
        private Ellipse? _userMarker;

        // For 3D Globe
        private double orbitRadius = 5;
        private double orbitAngle = 0;
        private double verticalAngle = 0;
        private double zoomFactor = 1;
        private double orbitVelocity = 0;
        private double verticalVelocity = 0;
        private bool isMouseDown = false;
        private Point lastMousePosition;
        private QualityLevel quality = QualityLevel.High;

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
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            this.Loaded += MainWindow_Loaded;
            this.SizeChanged += MainWindow_SizeChanged;

            // Initialize 3D Globe
            InitializeGlobe();
        }


        private void InitializeGlobe()
        {
            // OpenGL инициализируется в OpenGLControl
            // Важно: WindowsFormsHost/WinForms могут «съедать» колесо мыши, поэтому подписываемся на несколько источников.
            // Но мы НЕ подписываемся на this.MouseWheel, чтобы прокрутка из UI не долетала до глобуса.
            openGLHost.PreviewMouseWheel += MainWindow_MouseWheel; // tunneling событие
            openGLHost.MouseWheel += MainWindow_MouseWheel; // bubbling событие
            openGLControl.MouseWheel += OpenGLControl_MouseWheel; // обработка колеса мыши на WinForms контроле

            // Установка Win32 хука для колеса мыши
            _mouseHookProc = MouseHookCallback;
            _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, IntPtr.Zero, 0);

            openGLHost.MouseLeftButtonDown += Viewport3D_MouseLeftButtonDown;
            openGLHost.MouseLeftButtonUp += Viewport3D_MouseLeftButtonUp;
            openGLHost.MouseMove += Viewport3D_MouseMove;

            // Инициализация маркера локации (упрощённо, без 3D модели)
            CompositionTarget.Rendering += OnRenderingGlobe;

            openGLControl.CameraRotated += (yaw, pitch) =>
            {
                // Синхронизируем углы в MainWindow
                this.orbitAngle = yaw;
                this.verticalAngle = pitch;
            };
        }

        private void OpenGLControl_MouseWheel(object? sender, WinForms.MouseEventArgs e)
        {
            if (openGLHost.Visibility != Visibility.Visible)
                return;

            AdjustGlobeZoom(e.Delta);
        }

        private void AdjustGlobeZoom(int wheelDelta)
        {
            // wheelDelta обычно кратен 120
            double zoomSpeed = 0.001 * zoomFactor; // адаптивная скорость зума
            zoomFactor += wheelDelta * zoomSpeed;
            if (zoomFactor < 0.2) zoomFactor = 0.2;
            if (zoomFactor > 10) zoomFactor = 10;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL)
            {
                if (openGLHost.Visibility == Visibility.Visible)
                {
                    // Извлекаем delta из MSLLHOOKSTRUCT
                    MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    Point mousePos = new Point(hookStruct.pt_x, hookStruct.pt_y);

                    try
                    {
                        var hostHandle = openGLHost.Handle; // Handle WinForms контрола
                        var myHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

                        // 1. Проверяем, что наше окно активно (в фокусе)
                        IntPtr foregroundHwnd = GetForegroundWindow();
                        if (foregroundHwnd != myHwnd && !IsChild(myHwnd, foregroundHwnd))
                        {
                            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
                        }

                        // 2. Проверяем, что окно не свернуто
                        if (IsIconic(myHwnd))
                        {
                            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
                        }

                        // 3. Проверяем границы OpenGL области
                        if (GetWindowRect(hostHandle, out RECT hostRect))
                        {
                            Rect rect = new Rect(hostRect.Left, hostRect.Top, hostRect.Right - hostRect.Left, hostRect.Bottom - hostRect.Top);
                            
                            if (rect.Contains(mousePos))
                            {
                                int delta = (short)(hookStruct.mouseData >> 16);
                                AdjustGlobeZoom(delta);
                                return (IntPtr)1; // Блокируем только если мы активны и над OpenGL
                            }
                        }
                    }
                    catch
                    {
                        // В случае ошибки просто пропускаем
                    }
                }
            }
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }


        protected override void OnClosed(EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering2D;
            if (_mouseHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookHandle);
            }
            base.OnClosed(e);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsSystemThemeDark())
            {
                EnableDarkModeForWindow();
            }
            await LoadGeoJsonAsync();
            await GetUserLocationAsync();

            // Инициализация целей для плавных движений
            targetTranslateX = MapTranslateTransform.X;
            targetTranslateY = MapTranslateTransform.Y;
            targetScaleX = MapScaleTransform.ScaleX;
            targetScaleY = MapScaleTransform.ScaleY;

            // Центрирование камеры на местоположении пользователя с приближением (отложено)
            if (_hasLocation)
            {
                _ = Dispatcher.InvokeAsync(() => CenterOnUserLocation());
            }

            // Подписка на рендеринг для плавных движений 2D карты (по умолчанию 2D)
            CompositionTarget.Rendering += OnRendering2D;
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


        private async Task GetUserLocationAsync()
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetStringAsync("http://ip-api.com/json/");
                var json = JsonDocument.Parse(response);
                if (json.RootElement.TryGetProperty("lat", out var latProp) &&
                    json.RootElement.TryGetProperty("lon", out var lonProp))
                {
                    _currentLat = latProp.GetDouble();
                    _currentLon = lonProp.GetDouble();
                    _hasLocation = true;
                    await Dispatcher.InvokeAsync(() => UpdateUserPosition(_currentLat, _currentLon));
                }
            }
            catch (Exception ex)
            {
                // Log error if needed
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

            // Maintain 1:1 aspect ratio for the map to prevent stretching
            double mapWidth = canvasHeight * 1.0;
            double xOffset = (canvasWidth - mapWidth) / 2;

            // Clamp latitude to avoid infinity
            lat = Math.Max(-85, Math.Min(85, lat));

            // Mercator projection
            double x = (lon + 180) / 360 * mapWidth + xOffset;
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

        private void OnRendering2D(object? sender, EventArgs e)
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
                string json = await File.ReadAllTextAsync("Resources/countries_2d.geojson");
                _geoJsonData = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(json);
                DrawMap();
            }
            catch (Exception ex)
            {
                // Log error if needed
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
                // Log error if needed
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

            int step = 1; // Использовать все точки для полного разрешения
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
            if (_viewModel == null) return;
            ComboBoxItem item = MapTypeSelector.SelectedItem as ComboBoxItem;
            if (item != null)
            {
                _viewModel.MapTypeSelectorCommand.Execute(item.Content.ToString());
            }
        }

        private void ToggleAtmosphere_Checked(object sender, RoutedEventArgs e)
        {
            // Atmosphere not implemented in Earth3D
        }

        private void ToggleStars_Checked(object sender, RoutedEventArgs e)
        {
            // Stars are always visible in Earth3D
        }

        private void RotationSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Adjust rotation speed if needed
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            // Reset globe view
            orbitAngle = 0;
            verticalAngle = 0;
            zoomFactor = 1;
            // Обновление камеры в OpenGL
        }


        private void OnRenderingGlobe(object sender, EventArgs e)
        {
            orbitAngle += orbitVelocity;
            verticalAngle += verticalVelocity;
            verticalAngle = Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, verticalAngle));

            orbitVelocity *= 0.98;
            verticalVelocity *= 0.98;

            orbitAngle += 0.0001;

            // Применяем zoomFactor к радиусу орбиты.
            // Чем больше zoomFactor, тем ближе камера (меньше радиус).
            // Базовый радиус 5.0, минимальный пусть будет 1.2 (чуть больше радиуса Земли 1.0), максимальный 20.0.
            double currentRadius = 5.0 / zoomFactor;
            currentRadius = Math.Clamp(currentRadius, 1.1, 20.0);

            double x = currentRadius * Math.Cos(orbitAngle) * Math.Cos(verticalAngle);
            double y = currentRadius * Math.Sin(verticalAngle);
            double z = currentRadius * Math.Sin(orbitAngle) * Math.Cos(verticalAngle);

            // Обновление камеры в OpenGL
            openGLControl.UpdateCamera(new System.Numerics.Vector3((float)x, (float)y, (float)z));

            // Обновление направления Солнца (для атмосферы)
            var sunDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(1, 0, 1));
            openGLControl.UpdateSunDirection(sunDir);
        }

        private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Это событие вызывается только для openGLHost (PreviewMouseWheel/MouseWheel)
            if (openGLHost.Visibility == Visibility.Visible)
            {
                AdjustGlobeZoom(e.Delta);
                e.Handled = true;
            }
        }

        private void Viewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isMouseDown = true;
            lastMousePosition = e.GetPosition(this);
            openGLHost.CaptureMouse();
            openGLControl.Focus();
        }

        private void Viewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isMouseDown = false;
            openGLHost.ReleaseMouseCapture();
            openGLControl.Focus();
        }

        private void Viewport3D_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown)
            {
                Point currentPosition = e.GetPosition(this);
                double deltaX = currentPosition.X - lastMousePosition.X;
                double deltaY = currentPosition.Y - lastMousePosition.Y;

                orbitVelocity += deltaX * 0.0005;
                verticalVelocity += deltaY * 0.0005;

                lastMousePosition = currentPosition;
                openGLControl.Focus();
            }
        }
    }
}
