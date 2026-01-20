using System;
using System.Collections.Generic;
using HysteryVPN.Models;
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
using System.Linq;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace HysteryVPN.Rendering
{
    public class MapboxStyleGlobe : HelixViewport3D
    {
        private GeometryModel3D earthModel;
        private ModelVisual3D earthVisual;
        private GeometryModel3D atmosphereGlow;
        private Model3DGroup skyboxModel;
        private DoubleAnimation rotationAnimation;
        private bool atmosphereVisible = true;
        private bool starsVisible = true;
        private MaterialGroup atmosphereMaterial;
        private Model3DGroup skyboxMaterialGroup;
        private DirectionalLight sunLight;
        private DirectionalLight backLight;
        private AmbientLight ambientLight;
        private double earthRotationAngle = 0;
    
        private double cameraDistance;
        private double theta;
        private double phi;
        private double pitch = 0; // Угол наклона камеры (в радианах)
        private Point3D lookAtPoint = new Point3D(0, 0, 0);
        private double targetTheta, targetPhi, targetDistance, targetPitch;
        private Point3D targetLookAtPoint = new Point3D(0, 0, 0);
        private double velocityTheta, velocityPhi, velocityDistance;
        private Vector3D velocityLookAt = new Vector3D(0, 0, 0);
        private bool isRotating = false;
        private bool isPanning = false;
        private Point lastMousePosition;
        private Point3D? rotationPivotPoint = null;
        private new const double RotationSensitivity = 0.005;
        private const double PanSensitivity = 0.001;
        private const double MinCameraDistance = 1.001;
        private const double MaxCameraDistance = 1000.0;
        private const double MinPhi = 0.01;
        private const double MaxPhi = Math.PI - 0.01;
        private const double MinPitch = 0;
        private const double MaxPitch = Math.PI / 3; // Ограничение наклона 60 градусов
        private const double Damping = 0.92;
        private const double SmoothFactor = 0.2;
        private const double LookAtSmoothFactor = 0.25;
        private bool isAnimatingToPoint = false;
        private Point3D animationTargetPoint;
        private double animationStartTime;
        private const double AnimationDuration = 1.0;
        private const double MaxVelocity = 0.1;


        public MapboxStyleGlobe()
        {
            this.Focusable = true;
            this.IsTabStop = true;
            InitializeCamera();
            CreateSkybox();
            CreateDarkEarth();
            CreateAtmosphericGlow();
            SetupLighting();

            // Добавить точку местоположения (Томск)
            AddUserLocation(56.5, 84.97);

            this.Loaded += (s, e) => SetupInteractions();

            // Добавьте эту строку:
            CompositionTarget.Rendering += OnRendering;
        }

        private void InitializeCamera()
        {
            cameraDistance = targetDistance = 5.0;
            theta = targetTheta = 0.0;
            phi = targetPhi = Math.PI / 2.0; // Смотрим на экватор
            pitch = targetPitch = 0;


            // В Google Earth стиле камера всегда смотрит на центр Земли
            lookAtPoint = targetLookAtPoint = new Point3D(0, 0, 0);
            velocityTheta = 0.0;
            velocityPhi = 0.0;
            velocityDistance = 0.0;
            velocityLookAt = new Vector3D(0, 0, 0);

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

            pc.NearPlaneDistance = 0.001;
            pc.FarPlaneDistance = 1000;

            UpdateCameraPosition();
        }

        private void CreateSkybox()
        {
            // Создание звёздного skybox из Earth3D
            var starSkybox = new StarSkybox();
            skyboxModel = starSkybox.CreateModel();

            var skyboxVisual = new ModelVisual3D { Content = skyboxModel };
            Children.Add(skyboxVisual);
        }


        private void CreateDarkEarth()
        {
            // Создание Земли с текстурой из Earth3D
            var earthSphereModel = new EarthSphere((int)QualityLevel.High);
            earthModel = earthSphereModel.CreateModel();
            earthModel.BackMaterial = earthModel.Material; // Дублируем материал для обратной стороны
            earthModel.Transform = new ScaleTransform3D(-1, 1, 1); // Выворачиваем сферу

            earthVisual = new ModelVisual3D { Content = earthModel };
            Children.Add(earthVisual);
        }


        private Point3D LatLonToPoint3D(double lat, double lon, double radius)
        {
            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            double x = radius * Math.Cos(latRad) * Math.Cos(lonRad);
            double y = radius * Math.Sin(latRad);
            double z = radius * Math.Cos(latRad) * Math.Sin(lonRad);

            return new Point3D(x, y, z);
        }

        private void CreateAtmosphericGlow()
        {
            var atmosphericScattering = new AtmosphericScattering();

            // Исправление: безопасное приведение типа
            if (earthModel.Geometry is MeshGeometry3D earthMesh)
            {
                atmosphereGlow = atmosphericScattering.CreateAtmosphericGlow(earthMesh);
                atmosphereMaterial = atmosphereGlow.Material as MaterialGroup;

                var atmosphereVisual = new ModelVisual3D { Content = atmosphereGlow };
                Children.Add(atmosphereVisual);
            }
        }


        private void SetupLighting()
        {
            // Минимальное освещение для тёмной темы
            Lights.Children.Clear();
            
            ambientLight = new AmbientLight(System.Windows.Media.Color.FromRgb(5, 5, 10));
            Lights.Children.Add(ambientLight);

            // Яркое "Солнце" (белый или холодный синий)
            sunLight = new DirectionalLight(Colors.White, new Vector3D(-1, -0.3, -1));
            Lights.Children.Add(sunLight);

            // Слабая подсветка с обратной стороны
            backLight = new DirectionalLight(System.Windows.Media.Color.FromRgb(50, 50, 70), new Vector3D(0.5, 0.2, 1));
            Lights.Children.Add(backLight);

            UpdateSunPosition();
        }

        private void UpdateSunPosition()
        {
            if (sunLight == null) return;

            // Получаем текущее время UTC
            DateTime now = DateTime.UtcNow;
            
            // Вычисляем угол поворота Земли (0 градусов в полдень на Гринвиче)
            // В 12:00 UTC солнце должно быть над 0 долготой.
            // В 00:00 UTC солнце должно быть над 180 долготой.
            
            double hours = now.Hour + now.Minute / 60.0 + now.Second / 3600.0;
            
            // Угол в радианах. 12:00 UTC -> 0 rad, 00:00 UTC -> PI rad
            // Земля вращается с запада на восток, солнце "движется" с востока на запад.
            double longitudeRad = (hours - 12.0) * (2.0 * Math.PI / 24.0);
            
            // Учитываем наклон земной оси (примерно 23.44 градуса)
            // В зависимости от дня года склонение солнца меняется от -23.44 до +23.44
            int dayOfYear = now.DayOfYear;
            double declination = 23.44 * Math.Sin(2.0 * Math.PI * (dayOfYear - 81) / 365.0) * Math.PI / 180.0;

            // Вычисляем вектор направления солнца
            // x = cos(lat) * cos(lon)
            // y = sin(lat)
            // z = cos(lat) * sin(lon)
            // Но в нашей системе координат (из LatLonToPoint3D):
            // x = radius * Math.Cos(latRad) * Math.Cos(lonRad);
            // y = radius * Math.Sin(latRad);
            // z = radius * Math.Cos(latRad) * Math.Sin(lonRad);
            
            double sunX = Math.Cos(declination) * Math.Cos(longitudeRad);
            double sunY = Math.Sin(declination);
            double sunZ = Math.Cos(declination) * Math.Sin(longitudeRad);

            // Направление света - это вектор ОТ источника К объекту.
            // Поэтому инвертируем вектор положения солнца.
            sunLight.Direction = new Vector3D(-sunX, -sunY, -sunZ);
            
            // Обновляем backLight (слабая подсветка с противоположной стороны)
            if (backLight != null)
            {
                backLight.Direction = new Vector3D(sunX, sunY, sunZ);
            }
        }

        private void SetupInteractions()
        {
            Console.WriteLine("SetupInteractions called");
            if (this.CameraController != null)
            {
                this.CameraController.IsEnabled = false;
                Console.WriteLine("CameraController disabled, using custom logic");
            }
            else
            {
                Console.WriteLine("CameraController is null");
            }

            // Удалите старые подписки:
            // CompositionTarget.Rendering -= OnRendering;
            // this.PreviewMouseWheel += OnMouseWheel;
            
            // Добавьте новые подписки:
            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
            this.MouseRightButtonDown += OnMouseRightButtonDown;
            this.MouseMove += OnMouseMove;
            this.MouseLeftButtonUp += OnMouseButtonUp;
            this.MouseRightButtonUp += OnMouseButtonUp;
            this.PreviewMouseWheel += OnMouseWheel;
            this.MouseLeave += OnMouseLeave;
            
            this.Focus();
            Console.WriteLine("Interactions setup complete");
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isRotating = true;
            lastMousePosition = e.GetPosition(this);
            
            // Сбрасываем инерцию при новом клике
            velocityTheta = 0;
            velocityPhi = 0;

            this.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // В Google Earth стиле правая кнопка используется для наклона (pitch)
            isPanning = true;
            lastMousePosition = e.GetPosition(this);
            this.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var currentPos = e.GetPosition(this);
            var delta = currentPos - lastMousePosition;

            if (isRotating && e.LeftButton == MouseButtonState.Pressed)
            {
                // Google Earth стиль: левая кнопка вращает камеру вокруг Земли
                double sensitivity = RotationSensitivity;

                velocityTheta = -delta.X * sensitivity;
                velocityPhi = -delta.Y * sensitivity;

                targetTheta += velocityTheta;
                targetPhi += velocityPhi;
                targetPhi = Math.Max(MinPhi, Math.Min(MaxPhi, targetPhi));


                e.Handled = true;
            }
            else if (isPanning && e.RightButton == MouseButtonState.Pressed)
            {
                // Google Earth стиль: правая кнопка управляет наклоном (pitch)
                double pitchSensitivity = 0.005;
                targetPitch += delta.Y * pitchSensitivity;
                targetPitch = Math.Max(MinPitch, Math.Min(MaxPitch, targetPitch));

                e.Handled = true;
            }

            lastMousePosition = currentPos;
        }

        private void OnMouseButtonUp(object sender, MouseButtonEventArgs e)
        {
            isRotating = false;
            isPanning = false;
            this.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            isRotating = false;
            isPanning = false;
        }

        private void StartAutoRotation()
        {
            var transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
            earthModel.Transform = transform;

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
            if (!isRotating)
            {
                targetTheta += velocityTheta;
                targetPhi += velocityPhi;

                velocityTheta *= Damping;
                velocityPhi *= Damping;

                if (Math.Abs(velocityTheta) < 0.0001) velocityTheta = 0;
                if (Math.Abs(velocityPhi) < 0.0001) velocityPhi = 0;

            }

            // В Google Earth стиле нет панорамирования - камера всегда смотрит на центр
            // lookAtPoint всегда остается в центре Земли (0,0,0)

            if (Math.Abs(velocityDistance) > 0.0001)
            {
                targetDistance += velocityDistance;
                velocityDistance *= Damping;
            }

            targetPhi = Math.Max(MinPhi, Math.Min(MaxPhi, targetPhi));
            targetDistance = Math.Max(MinCameraDistance, Math.Min(MaxCameraDistance, targetDistance));

            // Интерполяция сферических координат для Google Earth стиля
            // Интерполяция по кратчайшему пути для Theta (долготы)
            double deltaTheta = targetTheta - theta;
            while (deltaTheta > Math.PI) deltaTheta -= 2 * Math.PI;
            while (deltaTheta < -Math.PI) deltaTheta += 2 * Math.PI;
            theta += deltaTheta * SmoothFactor;

            phi += (targetPhi - phi) * SmoothFactor;
            pitch += (targetPitch - pitch) * SmoothFactor;
            cameraDistance += (targetDistance - cameraDistance) * SmoothFactor;

            // Интерполяция lookAtPoint
            Vector3D deltaLookAt = targetLookAtPoint - lookAtPoint;
            lookAtPoint += deltaLookAt * LookAtSmoothFactor;

            if (isAnimatingToPoint)
            {
                double elapsed = (Environment.TickCount - animationStartTime) / 1000.0;
                double progress = Math.Min(elapsed / AnimationDuration, 1.0);
                progress = progress * progress * (3 - 2 * progress);

                if (progress >= 1.0)
                {
                    isAnimatingToPoint = false;
                    theta = targetTheta;
                    phi = targetPhi;
                    pitch = targetPitch;
                    cameraDistance = targetDistance;
                }
            }

            // Rotate Earth
            earthRotationAngle += 0.0001;
            if (earthModel != null)
            {
                earthModel.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), earthRotationAngle * 180 / Math.PI));
            }

            UpdateCameraPosition();
            UpdateAtmosphericGlow();
            UpdateSunPosition();
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
            // For now, stars are always visible as part of the skybox model
            // Could be improved to toggle visibility
        }


        public void SetRotationSpeed(double speed)
        {
            if (rotationAnimation != null && speed > 0)
            {
                rotationAnimation.Duration = TimeSpan.FromSeconds(120 / speed);
                // Restart animation with new duration
                if (earthModel.Transform is RotateTransform3D transform && transform.Rotation is AxisAngleRotation3D rotation)
                {
                    rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, rotationAnimation);
                }
            }
            else if (rotationAnimation != null && speed == 0)
            {
                if (earthModel.Transform is RotateTransform3D transform && transform.Rotation is AxisAngleRotation3D rotation)
                {
                    rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
                }
            }
        }

        public void ResetView()
        {
            targetTheta = 0;
            targetPhi = Math.PI / 2;
            targetDistance = 5.0;
            targetPitch = 0;
            // В Google Earth стиле lookAtPoint всегда в центре
            targetLookAtPoint = new Point3D(0, 0, 0);
            isAnimatingToPoint = true;
            animationTargetPoint = new Point3D(0, 0, 0);
            animationStartTime = Environment.TickCount;

            velocityTheta = 0.0;
            velocityPhi = 0.0;
            velocityDistance = 0.0;
            velocityLookAt = new Vector3D(0, 0, 0);
        }

        public void FlyToLocation(double lat, double lon, double altitude = 2.0, double pitchDeg = 0)
        {
            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            // В Google Earth стиле камера всегда смотрит на центр Земли,
            // но мы рассчитываем углы так, чтобы камера была направлена к целевой точке на поверхности
            targetPhi = Math.PI / 2 - latRad; // Широта определяет phi
            targetTheta = lonRad; // Долгота определяет theta
            targetDistance = Math.Max(MinCameraDistance, altitude);
            targetPitch = pitchDeg * Math.PI / 180.0;

            // Look-at point остается в центре для орбитального движения
            targetLookAtPoint = new Point3D(0, 0, 0);

            isAnimatingToPoint = true;
            // Для анимации используем центр как целевую точку
            animationTargetPoint = new Point3D(0, 0, 0);
            animationStartTime = Environment.TickCount;
        }

        public void AddUserLocation(double lat, double lon)
        {
            // Convert lat/lon to 3D coordinates like in Earth3D
            double phi = (90 - lat) * Math.PI / 180;
            double theta = (lon + 180) * Math.PI / 180;

            double radius = 1.01; // Slightly above surface
            double x = -radius * Math.Sin(phi) * Math.Cos(theta);
            double y = radius * Math.Cos(phi);
            double z = radius * Math.Sin(phi) * Math.Sin(theta);

            // Create marker using MarkerSphere
            MarkerSphere markerSphere = new MarkerSphere();
            var locationMarker = markerSphere.CreateModel();
            locationMarker.Transform = new TranslateTransform3D(x, y, z);

            var markerVisual = new ModelVisual3D { Content = locationMarker };
            Children.Add(markerVisual);

            // Animation (Color blinking)
            var brush = (SolidColorBrush)((DiffuseMaterial)locationMarker.Material).Brush;
            ColorAnimation blinkAnimation = new ColorAnimation
            {
                From = Colors.Red,
                To = System.Windows.Media.Color.FromRgb(100, 0, 0),
                Duration = TimeSpan.FromSeconds(0.5),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, blinkAnimation);
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            this.Focus(); // Глобус готов принимать команды колесика сразу
            Console.WriteLine("Mouse entered, focusing");
        }




        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta != 0)
            {
                Console.WriteLine($"OnMouseWheel: Delta={e.Delta}, CurrentDistance={cameraDistance}");
                double zoomFactor = e.Delta > 0 ? 0.85 : 1.15;
                
                Point mousePos = e.GetPosition(this);
                
                // Use HelixViewport3D's built-in raycasting for better reliability
                var hits = Viewport3DHelper.FindHits(this.Viewport, mousePos);
                Point3D? surfacePoint = null;
                foreach (var hit in hits)
                {
                    if (hit.Visual == earthVisual)
                    {
                        surfacePoint = hit.Position;
                        break;
                    }
                }

                // Для всех случаев зума просто меняем дистанцию, сохраняя текущий фокус
                targetDistance = Math.Max(MinCameraDistance, Math.Min(MaxCameraDistance, targetDistance * zoomFactor));
                // targetLookAtPoint остается прежним
                
                velocityDistance = 0;
                e.Handled = true;
            }
        }

        private Point3D? GetSurfacePointUnderMouse(Point mousePos)
        {
            var pc = this.Camera as PerspectiveCamera;
            if (pc == null) return null;

            double aspect = this.ActualWidth / this.ActualHeight;
            double fovRad = pc.FieldOfView * Math.PI / 180.0;
            double tanFov = Math.Tan(fovRad / 2);
            double x = (mousePos.X / this.ActualWidth - 0.5) * 2 * tanFov * aspect;
            double y = (0.5 - mousePos.Y / this.ActualHeight) * 2 * tanFov;
            Vector3D localDirection = new Vector3D(x, y, -1);
            localDirection.Normalize();

            Vector3D look = pc.LookDirection;
            look.Normalize();
            Vector3D up = pc.UpDirection;
            up.Normalize();
            Vector3D right = Vector3D.CrossProduct(look, up);
            right.Normalize();

            Vector3D worldDirection = x * right + y * up - localDirection.Z * look;
            worldDirection.Normalize();

            Point3D O = pc.Position;
            Vector3D D = worldDirection;
            Point3D C = new Point3D(0, 0, 0);
            double R = 1.0;
            Vector3D OC = O - C;
            double b = Vector3D.DotProduct(OC, D);
            double c = OC.LengthSquared - R * R;
            double discriminant = b * b - c;
            if (discriminant >= 0)
            {
                double sqrtD = Math.Sqrt(discriminant);
                double t1 = -b - sqrtD;
                double t2 = -b + sqrtD;
                double t = (t1 > 0) ? t1 : (t2 > 0) ? t2 : 0;
                if (t > 0)
                {
                    return O + t * D;
                }
            }

            return null;
        }


        private void UpdateCameraPosition()
        {
            if (this.Camera is PerspectiveCamera pc)
            {
                // Google Earth стиль: камера всегда смотрит на центр Земли (0,0,0)
                // Позиция камеры рассчитывается на основе сферических координат

                // 1. Рассчитываем позицию камеры на сфере вокруг центра
                double x = cameraDistance * Math.Sin(phi) * Math.Cos(theta);
                double y = cameraDistance * Math.Cos(phi);
                double z = cameraDistance * Math.Sin(phi) * Math.Sin(theta);

                pc.Position = new Point3D(x, y, z);

                // 2. Камера смотрит на lookAtPoint
                Vector3D lookDir = lookAtPoint - pc.Position;
                lookDir.Normalize();
                pc.LookDirection = lookDir;

                // 3. Рассчитываем UpDirection
                // На полюсах используем специальную логику для избежания gimbal lock
                Vector3D up;
                if (Math.Abs(y / cameraDistance) > 0.999) // Близко к полюсам
                {
                    up = new Vector3D(Math.Cos(theta), 0, Math.Sin(theta));
                }
                else
                {
                    // Стандартный расчет up-вектора
                    var right = Vector3D.CrossProduct(pc.LookDirection, new Vector3D(0, 1, 0));
                    right.Normalize();
                    up = Vector3D.CrossProduct(right, pc.LookDirection);
                }
                up.Normalize();

                // 4. Применяем Pitch (наклон) относительно локальной оси Right
                if (pitch != 0)
                {
                    var right = Vector3D.CrossProduct(pc.LookDirection, up);
                    right.Normalize();

                    var pitchQuaternion = new Quaternion(right, pitch * 180 / Math.PI);
                    var pitchMatrix = Matrix3D.Identity;
                    pitchMatrix.Rotate(pitchQuaternion);

                    pc.LookDirection = pitchMatrix.Transform(pc.LookDirection);
                    up = pitchMatrix.Transform(up);
                }

                pc.UpDirection = up;
                this.InvalidateVisual();
            }
        }
    }
}
