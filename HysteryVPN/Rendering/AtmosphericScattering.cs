using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Color = System.Windows.Media.Color;

namespace HysteryVPN.Rendering
{
    public class AtmosphericScattering
    {
        // Параметры атмосферы (как у Mapbox)
        private readonly Color atmosphereColor = Colors.White;
        private readonly double earthRadius = 1.0;
        private readonly double atmosphereRadius = 1.01;
        private readonly double scatteringCoefficient = 0.1;

        public GeometryModel3D CreateAtmosphericGlow(MeshGeometry3D earthMesh)
        {
            var atmosphereMesh = CreateAtmosphereMesh(earthMesh);

            // Создание материала с градиентной прозрачностью
            var material = CreateAtmosphereMaterial();

            return new GeometryModel3D(atmosphereMesh, material);
        }

        private MeshGeometry3D CreateAtmosphereMesh(MeshGeometry3D earthMesh)
        {
            // Создаём атмосферу как слегка увеличенную копию Земли
            var atmosphereMesh = earthMesh.Clone();

            // Увеличиваем радиус для атмосферы
            var positions = new Point3DCollection();
            foreach (var point in atmosphereMesh.Positions)
            {
                // Нормализуем и увеличиваем радиус
                var length = Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z);
                var scale = atmosphereRadius / earthRadius;

                positions.Add(new Point3D(
                    point.X * scale,
                    point.Y * scale,
                    point.Z * scale
                ));
            }

            atmosphereMesh.Positions = positions;
            return atmosphereMesh;
        }

        private MaterialGroup CreateAtmosphereMaterial()
        {
            var materialGroup = new MaterialGroup();

            // Основной слой атмосферы
            var glowColor = Color.FromArgb(40, 200, 230, 255);
            var emissive = new EmissiveMaterial(new SolidColorBrush(glowColor));
            materialGroup.Children.Add(emissive);

            // Слой Bloom (мягкое внешнее свечение)
            // В WPF мы имитируем это через RadialGradientBrush на материале
            var bloomBrush = new RadialGradientBrush();
            bloomBrush.GradientStops.Add(new GradientStop(Color.FromArgb(60, 100, 200, 255), 0.85));
            bloomBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 50, 150, 255), 1.0));
            
            var bloomMaterial = new EmissiveMaterial(bloomBrush);
            materialGroup.Children.Add(bloomMaterial);

            return materialGroup;
        }

        // Метод для динамического обновления свечения в зависимости от угла зрения
        public void UpdateAtmosphericGlow(GeometryModel3D atmosphere, Vector3D viewDirection)
        {
            if (atmosphere.Material is MaterialGroup materialGroup)
            {
                // Вычисляем интенсивность свечения на основе угла между нормалью и направлением взгляда
                double glowIntensity = CalculateGlowIntensity(viewDirection);

                // Обновляем материалы
                foreach (var material in materialGroup.Children)
                {
                    if (material is EmissiveMaterial emissiveMaterial && emissiveMaterial.Brush is RadialGradientBrush radialBrush)
                    {
                        var newStops = new GradientStopCollection();
                        foreach (var stop in radialBrush.GradientStops)
                        {
                            var newColor = Color.FromArgb(
                                (byte)Math.Min(255, stop.Color.A * glowIntensity),
                                stop.Color.R,
                                stop.Color.G,
                                stop.Color.B
                            );
                            newStops.Add(new GradientStop(newColor, stop.Offset));
                        }
                        radialBrush.GradientStops = newStops;
                    }
                }
            }
        }

        private double CalculateGlowIntensity(Vector3D viewDirection)
        {
            // Интенсивность свечения максимальна на краях (limb darkening)
            // Упрощённая модель рассеяния Рэлея
            double angle = Vector3D.AngleBetween(viewDirection, new Vector3D(0, 0, -1));
            double normalizedAngle = angle / 90.0; // Нормализуем от 0 до 1

            // Кривая интенсивности (пик на краях)
            return Math.Pow(Math.Sin(normalizedAngle * Math.PI), 0.7);
        }
    }
}
