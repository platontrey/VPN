using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Input;
using HelixToolkit.Wpf;
using System.IO;
using System.Text.Json;

namespace HysteryVPN

{
    public class MapboxStyleGlobe : HelixViewport3D
    {
        private SphereVisual3D earthSphere;
        private GeometryModel3D atmosphereGlow;
        private SphereVisual3D skybox;
        private DoubleAnimation rotationAnimation;
        private bool atmosphereVisible = true;
        private bool starsVisible = true;
        private MaterialGroup atmosphereMaterial;
        private Material skyboxMaterial;
    
        // Для интерактивного управления камерой
        private bool isRotating = false;
        private bool isZooming = false;
        private Point lastMousePosition;
        private double cameraDistance;
        private double theta; // Азимут
        private double phi; // Высота
        private double rotationSpeedTheta = 0.0;
        private double rotationSpeedPhi = 0.0;
        private double zoomSpeed = 0.0;
        private const double friction = 0.95;
        private const double minSpeed = 0.001;

        // Целевые значения (куда хотим прийти)
        private double targetTheta, targetPhi, targetDistance;
        // Текущая скорость (для инерции)
        private double velocityTheta, velocityPhi, velocityDistance;
        // Коэффициенты (можно подстроить под себя)
        private const double Damping = 0.92; // Инерция: чем ближе к 1, тем дольше крутится
        private const double SmoothFactor = 0.15; // Плавность следования: чем меньше, тем "тяжелее" камера
        private const double SmoothFactorDistance = 0.05; // Коэффициент плавности для зума

        public MapboxStyleGlobe()
        {
            this.Focusable = true;
            this.IsTabStop = true;
            InitializeCamera();
            CreateSkybox();
            CreateDarkEarth();
            CreateAtmosphericGlow();
            SetupLighting();

            this.Loaded += (s, e) => { Console.WriteLine("Loaded event fired"); SetupInteractions(); };
        }

        private void InitializeCamera()
        {
            cameraDistance = targetDistance = 5.0;
            theta = targetTheta = 0.0;
            phi = targetPhi = Math.PI / 2.0;
            velocityTheta = 0.0;
            velocityPhi = 0.0;
            velocityDistance = 0.0;

            // Вместо создания новой камеры, убедимся что текущая - перспективная
            PerspectiveCamera pc;
            if (!(this.Camera is PerspectiveCamera))
            {
                pc = new PerspectiveCamera { FieldOfView = 60 };
                this.Camera = pc;
            }
            else
            {
                pc = (PerspectiveCamera)this.Camera;
            }

            // ОЧЕНЬ ВАЖНО: позволяем камере видеть объекты очень близко
            pc.NearPlaneDistance = 0.01;
            pc.FarPlaneDistance = 1000;

            UpdateCameraPosition();
        }

        private void CreateSkybox()
        {
            // Создание сферического скайбокса
            var generator = new StarFieldGenerator();
            var starTexture = generator.GenerateStarField(2048, 1024, 5000); // 2:1 aspect для сферы

            var material = new EmissiveMaterial(new ImageBrush(starTexture));
            skyboxMaterial = material;

            skybox = new SphereVisual3D
            {
                Radius = 80, // Большой радиус для окружения сцены
                Material = material,
                BackMaterial = material, // Для видимости изнутри
                Center = new Point3D(0, 0, 0)
            };

            Children.Add(skybox);
        }


        private void CreateDarkEarth()
        {
            // Создание Земли с тёмной текстурой
            earthSphere = new SphereVisual3D
            {
                Radius = 1.0,
                Center = new Point3D(0, 0, 0),
                Material = CreateMapboxDarkMaterial()
            };

            Children.Add(earthSphere);
        }

        private Material CreateMapboxDarkMaterial()
        {
            var texture = GenerateGeoJsonTexture(2048, 1024);

            return new DiffuseMaterial(new ImageBrush(texture));
        }

        private WriteableBitmap GenerateGeoJsonTexture(int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);

            byte[] pixels = new byte[width * height * 4];
            Random rand = new Random();
            PerlinNoise noise = new PerlinNoise();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Преобразуем координаты пикселя в сферические координаты (Lat/Lon)
                    double lon = (double)x / width * Math.PI * 2;
                    double lat = (double)y / height * Math.PI;

                    // Получаем 3D координаты на поверхности сферы для выборки шума
                    // Это гарантирует отсутствие швов
                    double nx = Math.Sin(lat) * Math.Cos(lon);
                    double ny = Math.Sin(lat) * Math.Sin(lon);
                    double nz = Math.Cos(lat);

                    // И передайте три параметра в шум:
                    double continentNoise = noise.Perlin(nx * 3.0, ny * 3.0, nz * 3.0); 

                    // Определяем, земля это или вода
                    bool isLand = continentNoise > 0.1;

                    Color color;
                    if (isLand)
                    {
                        color = Color.FromRgb(25, 30, 38);
                    }
                    else
                    {
                        color = Color.FromRgb(10, 14, 20);
                    }

                    int index = (y * width + x) * 4;
                    pixels[index] = color.B;     // Blue
                    pixels[index + 1] = color.G; // Green
                    pixels[index + 2] = color.R; // Red
                    pixels[index + 3] = 255;     // Alpha
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            bitmap.Freeze();

            return bitmap;
        }





        private void CreateAtmosphericGlow()
        {
            var atmosphericScattering = new AtmosphericScattering();

            // Получаем mesh Земли
            var earthMesh = ((GeometryModel3D)earthSphere.Content).Geometry as MeshGeometry3D;

            // Создаём атмосферное свечение
            atmosphereGlow = atmosphericScattering.CreateAtmosphericGlow(earthMesh);
            atmosphereMaterial = atmosphereGlow.Material as MaterialGroup;

            var atmosphereVisual = new ModelVisual3D { Content = atmosphereGlow };
            Children.Add(atmosphereVisual);
        }

        private void SetupLighting()
        {
            // Минимальное освещение для тёмной темы
            Lights.Children.Clear();
            Lights.Children.Add(new AmbientLight(Color.FromRgb(5, 5, 10))); // Почти черный AmbientLight (чтобы тени были глубокими)

            // Яркое "Солнце" (белый или холодный синий)
            var sunLight = new DirectionalLight(Colors.White, new Vector3D(-1, -0.3, -1));
            Lights.Children.Add(sunLight);

            // Слабая подсветка с обратной стороны
            var backLight = new DirectionalLight(Color.FromRgb(50, 50, 70), new Vector3D(0.5, 0.2, 1));
            Lights.Children.Add(backLight);
        }

        private void SetupInteractions()
        {
            Console.WriteLine("SetupInteractions called");
            if (this.CameraController != null)
            {
                // Отключаем стандартный контроллер, используем кастомную логику
                this.CameraController.IsEnabled = false;
                Console.WriteLine("CameraController disabled, using custom logic");
            }
            else
            {
                Console.WriteLine("CameraController is null");
            }

            // Оставляем жесты
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += OnRendering;
            this.PreviewMouseWheel += OnMouseWheel;
            Console.WriteLine("OnRendering subscribed, PreviewMouseWheel added");
        }

        private void StartAutoRotation()
        {
            var transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
            earthSphere.Transform = transform;

            rotationAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(120),
                RepeatBehavior = RepeatBehavior.Forever
            };

            ((AxisAngleRotation3D)transform.Rotation).BeginAnimation(
                AxisAngleRotation3D.AngleProperty, rotationAnimation);
        }

        private void OnRendering(object sender, EventArgs e)
        {
            // 1. Если мышь отпущена, продолжаем двигаться по инерции
            if (!isRotating)
            {
                targetTheta += velocityTheta;
                targetPhi += velocityPhi;

                // Затухание скорости
                velocityTheta *= Damping;
                velocityPhi *= Damping;
            }

            if (!isZooming)
            {
                targetDistance += velocityDistance;
                velocityDistance *= Damping;
            }

            // 2. Плавное ограничение наклона (чтобы не перевернуться через полюс)
            targetPhi = Math.Max(0.1, Math.Min(Math.PI - 0.1, targetPhi));
            targetDistance = Math.Max(1.1, Math.Min(20, targetDistance));

            // 3. Интерполяция (Lerp) - плавно приближаем текущие значения к целевым
            theta += (targetTheta - theta) * SmoothFactor;
            phi += (targetPhi - phi) * SmoothFactor;
            cameraDistance += (targetDistance - cameraDistance) * SmoothFactorDistance;

            // 4. Обновляем камеру и атмосферу
            UpdateCameraPosition();
            UpdateAtmosphericGlow();
        }

        private void UpdateAtmosphericGlow()
        {
            if (atmosphereVisible && atmosphereGlow != null)
            {
                var viewDirection = Camera.LookDirection;
                var atmosphericScattering = new AtmosphericScattering();
                atmosphericScattering.UpdateAtmosphericGlow(atmosphereGlow, viewDirection);
            }
        }

        public void SetAtmosphereVisible(bool visible)
        {
            atmosphereVisible = visible;
            if (atmosphereGlow != null)
            {
                atmosphereGlow.Material = visible ? atmosphereMaterial : null;
            }
        }

        public void SetStarsVisible(bool visible)
        {
            starsVisible = visible;
            if (skybox != null)
            {
                skybox.Material = visible ? skyboxMaterial : null;
                skybox.BackMaterial = visible ? skyboxMaterial : null;
            }
        }


        public void SetRotationSpeed(double speed)
        {
            if (rotationAnimation != null && speed > 0)
            {
                rotationAnimation.Duration = TimeSpan.FromSeconds(120 / speed);
                // Restart animation with new duration
                if (earthSphere.Transform is RotateTransform3D transform && transform.Rotation is AxisAngleRotation3D rotation)
                {
                    rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, rotationAnimation);
                }
            }
            else if (rotationAnimation != null && speed == 0)
            {
                if (earthSphere.Transform is RotateTransform3D transform && transform.Rotation is AxisAngleRotation3D rotation)
                {
                    rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
                }
            }
        }

        public void ResetView()
        {
            cameraDistance = targetDistance = 5.0;
            theta = targetTheta = 0.0;
            phi = targetPhi = Math.PI / 2.0;
            velocityTheta = 0.0;
            velocityPhi = 0.0;
            velocityDistance = 0.0;
            UpdateCameraPosition();
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            this.Focus(); // Глобус готов принимать команды колесика сразу
            Console.WriteLine("Mouse entered, focusing");
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            this.Focus();
            if (e.LeftButton == MouseButtonState.Pressed && !Keyboard.IsKeyDown(Key.LeftShift))
            {
                isRotating = true;
                lastMousePosition = e.GetPosition(this);
                this.CaptureMouse();
                e.Handled = true;
            }
            else if (e.RightButton == MouseButtonState.Pressed)
            {
                isZooming = true;
                lastMousePosition = e.GetPosition(this);
                this.CaptureMouse();
                e.Handled = true;
            }
            // Не вызываем base.OnMouseDown(e), чтобы избежать встроенных обработчиков
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isRotating)
            {
                var currentPos = e.GetPosition(this);
                var delta = currentPos - lastMousePosition;

                double sensitivity = 0.01;
                // Накапливаем скорость для инерции
                velocityTheta = -delta.X * sensitivity;
                velocityPhi = delta.Y * sensitivity;

                // Обновляем цель
                targetTheta += velocityTheta;
                targetPhi += velocityPhi;

                lastMousePosition = currentPos;
                e.Handled = true;
            }
            else if (isZooming)
            {
                var currentPos = e.GetPosition(this);
                var delta = currentPos - lastMousePosition;

                // Чувствительность зума
                double zoomSensitivity = 0.003;

                // Инвертируйте знак, если зум идет не в ту сторону
                velocityDistance += -delta.Y * zoomSensitivity * cameraDistance;

                lastMousePosition = currentPos;
                e.Handled = true;
            }
            // Не вызываем base.OnMouseMove(e)
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            isRotating = false;
            isZooming = false;
            this.ReleaseMouseCapture();
            e.Handled = true;
            // Не вызываем base.OnMouseUp(e)
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomDelta = e.Delta > 0 ? -0.03 * cameraDistance : 0.03 * cameraDistance;
            velocityDistance += zoomDelta;
            Console.WriteLine($"MouseWheel: velocityDistance: {velocityDistance}");
            e.Handled = true;
        }

        private void UpdateCameraPosition()
        {
            if (this.Camera is PerspectiveCamera pc)
            {
                double x = cameraDistance * Math.Sin(phi) * Math.Cos(theta);
                double y = cameraDistance * Math.Cos(phi);
                double z = cameraDistance * Math.Sin(phi) * Math.Sin(theta);

                pc.Position = new Point3D(x, y, z);
                Console.WriteLine($"Camera position set to: {pc.Position}");

                // Камера всегда смотрит в центр (0,0,0)
                pc.LookDirection = new Vector3D(-x, -y, -z);

                // Стабильный UpDirection
                Vector3D worldUp = new Vector3D(0, 1, 0);
                Vector3D look = pc.LookDirection;
                look.Normalize();

                Vector3D right = Vector3D.CrossProduct(look, worldUp);
                if (right.LengthSquared < 1e-6) // Защита от "залипания" на полюсах
                {
                    // Если смотрим вертикально, используем другое направление для Right
                    right = new Vector3D(Math.Sin(theta), 0, -Math.Cos(theta));
                }
                right.Normalize();
                pc.UpDirection = Vector3D.CrossProduct(right, look);
                // Принудительная перерисовка Viewport
                this.InvalidateVisual();
            }
            else
            {
                Console.WriteLine("Camera is not PerspectiveCamera");
            }
        }
    }
}