using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Threading.Tasks;
using Color = System.Windows.Media.Color;

namespace HysteryVPN.Rendering;

/// <summary>
/// Базовый наследуемый класс для создания 3D сфер с различными параметрами.
/// </summary>
public abstract class Sphere
{
    /// <summary>
    /// Кэш для MeshGeometry3D по ключу (radius, segments).
    /// </summary>
    protected static readonly Dictionary<(double, int), MeshGeometry3D> MeshCache = new();

    /// <summary>
    /// Радиус сферы.
    /// </summary>
    protected double Radius { get; set; }

    /// <summary>
    /// Количество сегментов для генерации меша (влияет на гладкость сферы).
    /// </summary>
    protected int Segments { get; set; }

    /// <summary>
    /// Материал сферы.
    /// </summary>
    protected Material Material { get; set; }

    /// <summary>
    /// Конструктор базового класса Sphere.
    /// </summary>
    /// <param name="radius">Радиус сферы.</param>
    /// <param name="segments">Количество сегментов.</param>
    /// <param name="material">Материал сферы.</param>
    protected Sphere(double radius, int segments, Material material)
    {
        Radius = radius;
        Segments = segments;
        Material = material;
    }

    /// <summary>
    /// Создает и возвращает GeometryModel3D для сферы.
    /// </summary>
    /// <returns>GeometryModel3D сферы.</returns>
    public GeometryModel3D CreateModel()
    {
        var key = (Radius, Segments);
        if (!MeshCache.TryGetValue(key, out var mesh))
        {
            mesh = CreateMesh();
            MeshCache[key] = mesh;
        }

        var model = new GeometryModel3D(mesh, Material);
        // Заморозить материал если возможно
        if (Material is Freezable freezableMaterial && !freezableMaterial.IsFrozen)
        {
            freezableMaterial.Freeze();
        }
        return model;
    }

    /// <summary>
    /// Создает MeshGeometry3D для сферы.
    /// </summary>
    /// <returns>MeshGeometry3D сферы.</returns>
    protected MeshGeometry3D CreateMesh()
    {
        MeshGeometry3D mesh = new MeshGeometry3D();

        // Создание вершин
        for (int i = 0; i <= Segments; i++)
        {
            double phi = Math.PI * i / Segments;
            for (int j = 0; j <= Segments; j++)
            {
                double theta = 2 * Math.PI * j / Segments;
                double x = Radius * Math.Sin(phi) * Math.Cos(theta);
                double y = Radius * Math.Cos(phi);
                double z = Radius * Math.Sin(phi) * Math.Sin(theta);
                mesh.Positions.Add(new Point3D(x, y, z));
                // Texture coordinates without V flip (Earth uses its own mesh)
                mesh.TextureCoordinates.Add(new System.Windows.Point((double)j / Segments, (double)i / Segments));
            }
        }

        // Создание треугольников
        for (int i = 0; i < Segments; i++)
        {
            for (int j = 0; j < Segments; j++)
            {
                int index = i * (Segments + 1) + j;
                // Correct winding order (front faces outward)
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + Segments + 1);

                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + Segments + 2);
                mesh.TriangleIndices.Add(index + Segments + 1);
            }
        }

        // Заморозить меш для оптимизации
        mesh.Freeze();
        return mesh;
    }
}

/// <summary>
/// Класс для сферы Земли с текстурой.
/// </summary>
public class EarthSphere : Sphere
{
    /// <summary>
    /// Конструктор для сферы Земли.
    /// </summary>
    public EarthSphere(int segments = 64) : base(1, segments, LoadEarthMaterial()) { }

    /// <summary>
    /// Загружает материал для Земли (текстура worldmap.png).
    /// </summary>
    /// <returns>Материал с текстурой Земли.</returns>
    private static Material LoadEarthMaterial()
    {
        try
        {
            ImageBrush brush = new ImageBrush();
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri("worldmap.png", UriKind.Relative);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze(); // Оптимизация: сделать изображение неизменяемым для лучшей производительности
            brush.ImageSource = bitmap;
            brush.Freeze(); // Заморозить brush тоже
            return new DiffuseMaterial(brush);
        }
        catch
        {
            // Fallback to solid color if image not found
            return new DiffuseMaterial(new SolidColorBrush(Colors.Blue));
        }
    }
}

/// <summary>
/// Класс для атмосферы планеты (вариант 5: отдельная сфера с мягким свечением).
/// </summary>
public class AtmosphereSphere : Sphere
{
    public AtmosphereSphere(double planetRadius, int segments = 64)
        : base(planetRadius * 1.03, segments, CreateAtmosphereMaterial()) { }

    private static Material CreateAtmosphereMaterial()
    {
        // Эмиссивное голубое свечение для атмосферы (приглушённое)
        var color = System.Windows.Media.Color.FromRgb(30, 45, 64);
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var material = new EmissiveMaterial(brush);
        material.Freeze();
        return material;
    }
}

/// <summary>
/// Класс для простой сферы с однотонным цветом.
/// </summary>
public class SimpleSphere : Sphere
{
    /// <summary>
    /// Конструктор для простой сферы.
    /// </summary>
    /// <param name="radius">Радиус сферы.</param>
    /// <param name="color">Цвет сферы.</param>
    public SimpleSphere(double radius, Color color) : base(radius, 32, new DiffuseMaterial(new SolidColorBrush(color))) { }
}

/// <summary>
/// Класс для сферы с пользовательским материалом.
/// </summary>
public class CustomSphere : Sphere
{
    /// <summary>
    /// Конструктор для сферы с пользовательским материалом.
    /// </summary>
    /// <param name="radius">Радиус сферы.</param>
    /// <param name="segments">Количество сегментов.</param>
    /// <param name="material">Пользовательский материал.</param>
    public CustomSphere(double radius, int segments, Material material) : base(radius, segments, material) { }
}

/// <summary>
/// Класс для маркера - маленькой красной сферы.
/// </summary>
public class MarkerSphere : Sphere
{
    /// <summary>
    /// Конструктор для маркера.
    /// </summary>
    public MarkerSphere() : base(0.01, 8, new DiffuseMaterial(new SolidColorBrush(Colors.Red))) { } // Уменьшен радиус и сегменты для оптимизации

    /// <summary>
    /// Создает и возвращает GeometryModel3D для маркера без заморозки материала для анимации.
    /// </summary>
    /// <returns>GeometryModel3D маркера.</returns>
    public new GeometryModel3D CreateModel()
    {
        var key = (Radius, Segments);
        if (!MeshCache.TryGetValue(key, out var mesh))
        {
            mesh = CreateMesh();
            MeshCache[key] = mesh;
        }

        var model = new GeometryModel3D(mesh, Material);
        // Не замораживать материал для маркера, чтобы позволить анимацию
        return model;
    }
}

/// <summary>
/// Класс для звёздного skybox.
/// </summary>
public class StarSkybox
{
    /// <summary>
    /// Создает и возвращает Model3DGroup для звёздного skybox.
    /// </summary>
    /// <returns>Model3DGroup skybox.</returns>
    public Model3DGroup CreateModel()
    {
        Model3DGroup group = new Model3DGroup();

        // Фоновая сфера
        MeshGeometry3D bgMesh = CreateSphereMesh(20, 32);
        bgMesh.Freeze(); // Заморозить для оптимизации
        var bgMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)));
        bgMaterial.Freeze(); // Заморозить материал
        GeometryModel3D bgModel = new GeometryModel3D(bgMesh, bgMaterial);
        bgModel.BackMaterial = bgModel.Material;
        group.Children.Add(bgModel);

        // Процедурные звёзды
        var starsModel = ProceduralStarGenerator.Generate();
        group.Children.Add(starsModel);

        return group;
    }

    /// <summary>
    /// Создает меш для сферы.
    /// </summary>
    /// <param name="radius">Радиус сферы.</param>
    /// <param name="segments">Количество сегментов.</param>
    /// <param name="offset">Смещение центра.</param>
    /// <returns>MeshGeometry3D.</returns>
    private MeshGeometry3D CreateSphereMesh(double radius, int segments, Point3D offset = default)
    {
        MeshGeometry3D mesh = new MeshGeometry3D();

        for (int i = 0; i <= segments; i++)
        {
            double phi = Math.PI * i / segments;
            for (int j = 0; j <= segments; j++)
            {
                double theta = 2 * Math.PI * j / segments;
                double x = radius * Math.Sin(phi) * Math.Cos(theta) + offset.X;
                double y = radius * Math.Cos(phi) + offset.Y;
                double z = radius * Math.Sin(phi) * Math.Sin(theta) + offset.Z;
                mesh.Positions.Add(new Point3D(x, y, z));
                mesh.TextureCoordinates.Add(new System.Windows.Point((double)j / segments, (double)i / segments));
            }
        }

        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                int index = i * (segments + 1) + j;
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + segments + 1);

                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + segments + 2);
                mesh.TriangleIndices.Add(index + segments + 1);
            }
        }

        return mesh;
    }
}

/// <summary>
/// Класс для генерации процедурных звёзд.
/// </summary>
public class ProceduralStarGenerator
{
    private const int StarCount = 4000;

    /// <summary>
    /// Генерирует один GeometryModel3D для всех звёзд.
    /// </summary>
    /// <returns>Модель звёзд.</returns>
    public static GeometryModel3D Generate()
    {
        MeshGeometry3D combinedMesh = new MeshGeometry3D();
        Random rand = new Random(42); // Фиксированный seed для一致ности

        for (int i = 0; i < StarCount; i++)
        {
            double phi = rand.NextDouble() * Math.PI;
            double theta = rand.NextDouble() * 2 * Math.PI;
            double x = 10 * Math.Sin(phi) * Math.Cos(theta);
            double y = 10 * Math.Cos(phi);
            double z = 10 * Math.Sin(phi) * Math.Sin(theta);

            double starRadius = rand.NextDouble() * 0.008 + 0.002; // Размер звезды от 0.002 до 0.01
            AddSphereToMesh(combinedMesh, starRadius, 4, new Point3D(x, y, z));
        }

        combinedMesh.Freeze(); // Заморозить меш для оптимизации

        // Все звёзды белые с вариациями яркости, но поскольку один материал, используем средний
        var color = Color.FromRgb(255, 255, 255); // Белый
        var brush = new SolidColorBrush(color);
        brush.Freeze(); // Заморозить brush
        var material = new EmissiveMaterial(brush);
        material.Freeze(); // Заморозить материал
        var model = new GeometryModel3D(combinedMesh, material);
        model.BackMaterial = model.Material;
        return model;
    }

    /// <summary>
    /// Добавляет сферу в существующий меш.
    /// </summary>
    /// <param name="mesh">Меш для добавления.</param>
    /// <param name="radius">Радиус сферы.</param>
    /// <param name="segments">Количество сегментов.</param>
    /// <param name="offset">Смещение центра.</param>
    private static void AddSphereToMesh(MeshGeometry3D mesh, double radius, int segments, Point3D offset)
    {
        int startIndex = mesh.Positions.Count;

        for (int i = 0; i <= segments; i++)
        {
            double phi = Math.PI * i / segments;
            for (int j = 0; j <= segments; j++)
            {
                double theta = 2 * Math.PI * j / segments;
                double x = radius * Math.Sin(phi) * Math.Cos(theta) + offset.X;
                double y = radius * Math.Cos(phi) + offset.Y;
                double z = radius * Math.Sin(phi) * Math.Sin(theta) + offset.Z;
                mesh.Positions.Add(new Point3D(x, y, z));
                mesh.TextureCoordinates.Add(new System.Windows.Point((double)j / segments, (double)i / segments));
            }
        }

        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                int index = startIndex + i * (segments + 1) + j;
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + segments + 1);

                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + segments + 2);
                mesh.TriangleIndices.Add(index + segments + 1);
            }
        }
    }

    /// <summary>
    /// Создает меш для сферы.
    /// </summary>
    /// <param name="radius">Радиус сферы.</param>
    /// <param name="segments">Количество сегментов.</param>
    /// <param name="offset">Смещение центра.</param>
    /// <returns>MeshGeometry3D.</returns>
    private static MeshGeometry3D CreateSphereMesh(double radius, int segments, Point3D offset = default)
    {
        MeshGeometry3D mesh = new MeshGeometry3D();

        for (int i = 0; i <= segments; i++)
        {
            double phi = Math.PI * i / segments;
            for (int j = 0; j <= segments; j++)
            {
                double theta = 2 * Math.PI * j / segments;
                double x = radius * Math.Sin(phi) * Math.Cos(theta) + offset.X;
                double y = radius * Math.Cos(phi) + offset.Y;
                double z = radius * Math.Sin(phi) * Math.Sin(theta) + offset.Z;
                mesh.Positions.Add(new Point3D(x, y, z));
                mesh.TextureCoordinates.Add(new System.Windows.Point((double)j / segments, (double)i / segments));
            }
        }

        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                int index = i * (segments + 1) + j;
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + segments + 1);

                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + segments + 2);
                mesh.TriangleIndices.Add(index + segments + 1);
            }
        }

        return mesh;
    }
}
