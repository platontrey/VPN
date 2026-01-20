using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media;

namespace Earth3D;

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

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private OrthographicCamera camera;
    private Model3DGroup modelGroup;
    private double orbitRadius = 5;
    private double orbitAngle = 0;
    private double verticalAngle = 0;
    private double earthRotationAngle = 0;
    private double zoomFactor = 1;
    private double orbitVelocity = 0;
    private double verticalVelocity = 0;
    private bool isMouseDown = false;
    private Point lastMousePosition;
    private GeometryModel3D? locationMarker;
    private QualityLevel quality = QualityLevel.High; // Настраиваемое качество

    public MainWindow()
    {
        InitializeComponent();
        this.Focusable = true; // Делаем окно фокусируемым для обработки клавиш
        camera = (OrthographicCamera)viewport3D.Camera;
        CreateStarSkybox();
        modelGroup = (Model3DGroup)((ModelVisual3D)viewport3D.Children[1]).Content;
        CreateEarthSphere();
        this.MouseWheel += MainWindow_MouseWheel;
        viewport3D.MouseLeftButtonDown += Viewport3D_MouseLeftButtonDown;
        viewport3D.MouseLeftButtonUp += Viewport3D_MouseLeftButtonUp;
        viewport3D.MouseMove += Viewport3D_MouseMove;
        this.KeyDown += MainWindow_KeyDown; // Добавляем обработчик клавиш для изменения качества

        _ = SetupLocationMarker();
        CompositionTarget.Rendering += OnRendering; // Используем CompositionTarget для плавной анимации
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.D1: SetQuality(QualityLevel.Low); break;
            case Key.D2: SetQuality(QualityLevel.Medium); break;
            case Key.D3: SetQuality(QualityLevel.High); break;
            case Key.D4: SetQuality(QualityLevel.Ultra); break;
        }
    }

    private void SetQuality(QualityLevel newQuality)
    {
        if (quality == newQuality) return;
        quality = newQuality;
        // Очищаем старую модель Земли
        modelGroup.Children.Clear();
        // Пересоздаем
        CreateEarthSphere();
        // Добавляем маркер обратно, если он был
        if (locationMarker != null)
        {
            modelGroup.Children.Add(locationMarker);
        }
    }

    private void OnRendering(object sender, EventArgs e)
    {
        // Apply inertia
        orbitAngle += orbitVelocity;
        verticalAngle += verticalVelocity;
        // Инвертируем вертикальный угол камеры, чтобы движение мыши
        // совпадало с визуальной ориентацией Земли (север/юг)
        verticalAngle = Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, verticalAngle));

        // Dampen velocity (weaker inertia)
        orbitVelocity *= 0.98;
        verticalVelocity *= 0.98;

        // Rotate Earth
        earthRotationAngle += 0.0001;
        modelGroup.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), earthRotationAngle * 180 / Math.PI));

        // Камера вращается вместе с Землей
        orbitAngle += 0.0001;

        double x = orbitRadius * Math.Cos(orbitAngle) * Math.Cos(verticalAngle);
        double y = orbitRadius * Math.Sin(verticalAngle);
        double z = orbitRadius * Math.Sin(orbitAngle) * Math.Cos(verticalAngle);
        camera.Position = new Point3D(x, y, z);
        camera.LookDirection = new Vector3D(-x, -y, -z);

        // Обновляем позицию skybox, чтобы он всегда был в центре камеры
        var skyboxVisual = (ModelVisual3D)viewport3D.Children[0];
        skyboxVisual.Transform = new TranslateTransform3D(new Vector3D(camera.Position.X, camera.Position.Y, camera.Position.Z));
    }

    private async Task SetupLocationMarker()
    {
        try
        {
            var (lat, lon) = await GetLocationByIp();
            AddBlinkingMarker(lat, lon);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка получения местоположения: {ex.Message}");
        }
    }

    private async Task<(double lat, double lon)> GetLocationByIp()
    {
        using HttpClient client = new HttpClient();
        string json = await client.GetStringAsync("http://ip-api.com/json/");
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (root.GetProperty("status").GetString() == "success")
        {
            double lat = root.GetProperty("lat").GetDouble();
            double lon = root.GetProperty("lon").GetDouble();
            return (lat, lon);
        }
        throw new Exception("Не удалось получить данные от API");
    }

    private void AddBlinkingMarker(double lat, double lon)
    {
        // Convert lat/lon to 3D coordinates
        // Latitude: -90 to 90, Longitude: -180 to 180
        // In our sphere generation:
        // phi = Math.PI * i / segments (0 to PI, where 0 is North Pole)
        // theta = 2 * Math.PI * j / segments (0 to 2PI)

        // Географическая формула остаётся канонической
        // Инверсия выполняется только в UV, не в геометрии
        double phi = (90 - lat) * Math.PI / 180;
        double theta = (lon + 180) * Math.PI / 180;

        double radius = 1.01; // Lower the marker
        // Компенсация зеркального ScaleTransform3D(-1,1,1) у Земли
        double x = -radius * Math.Sin(phi) * Math.Cos(theta);
        double y =  radius * Math.Cos(phi);
        double z =  radius * Math.Sin(phi) * Math.Sin(theta);

        // Create marker using MarkerSphere
        MarkerSphere markerSphere = new MarkerSphere();
        locationMarker = markerSphere.CreateModel();
        locationMarker.Transform = new TranslateTransform3D(x, y, z);
        // Removed BackMaterial to avoid seeing it through the globe
        modelGroup.Children.Add(locationMarker);

        // Get the brush for animation
        SolidColorBrush brush = (SolidColorBrush)((DiffuseMaterial)locationMarker.Material).Brush;

        // Animation (Color blinking instead of opacity)
        ColorAnimation blinkAnimation = new ColorAnimation
        {
            From = Colors.Red,
            To = Color.FromRgb(100, 0, 0), // Dark red
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, blinkAnimation);
    }

    private void CreateStarSkybox()
    {
        StarSkybox skybox = new StarSkybox();
        ModelVisual3D skyboxVisual = new ModelVisual3D();
        skyboxVisual.Content = skybox.CreateModel();
        viewport3D.Children.Insert(0, skyboxVisual);
    }

    private void CreateEarthSphere()
    {
        // Используем EarthSphere с настраиваемым качеством
        EarthSphere earthSphere = new EarthSphere((int)quality);
        GeometryModel3D earthModel = earthSphere.CreateModel();
        earthModel.BackMaterial = earthModel.Material; // Дублируем материал для обратной стороны
        earthModel.Transform = new ScaleTransform3D(-1, 1, 1); // Выворачиваем сферу
        modelGroup.Children.Add(earthModel);
    }

    private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        zoomFactor += e.Delta * 0.002;
        if (zoomFactor < 1) zoomFactor = 1;
        if (zoomFactor > 100) zoomFactor = 100;
        camera.Width = 10 / zoomFactor;
    }

    private void Viewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        isMouseDown = true;
        lastMousePosition = e.GetPosition(this);
        viewport3D.CaptureMouse();
    }

    private void Viewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        isMouseDown = false;
        viewport3D.ReleaseMouseCapture();
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
        }
    }
}