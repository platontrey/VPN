using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Text.Json;
using HysteryVPN.Models;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using StbImageSharp;

namespace HysteryVPN.Rendering
{
    public class OpenGLRenderer
    {
        private GL _gl;
        private readonly IWindow _window;
        private float _radius = 5.0f;

        private uint _earthShaderProgram;
        private uint _atmosphereShaderProgram;
        private uint _starShaderProgram;
        private uint _locationShaderProgram;
        private uint _mapShaderProgram;

        // Uniform locations cache
        private int _earthViewLoc, _earthProjLoc, _earthModelLoc, _earthTexLoc, _earthSunLoc;
        private int _atmoViewLoc, _atmoProjLoc, _atmoModelLoc, _atmoSunLoc, _atmoCamLoc;
        private int _atmoEarthRadiusLoc, _atmoAtmosphereRadiusLoc, _atmoRayleighCoeffLoc, _atmoMieCoeffLoc, _atmoMieGLoc, _atmoRayleighScaleHeightLoc, _atmoMieScaleHeightLoc;
        private int _starViewLoc, _starProjLoc;
        private int _locationViewLoc, _locationProjLoc, _locationDistanceLoc;
        private int _mapViewLoc, _mapProjLoc, _mapModelLoc, _mapColorLoc;

        private uint _starVAO;
        private uint _starVBO;
        private int _starCount = 2000;

        private uint _sphereVAO;
        private uint _sphereVBO;
        private uint _sphereEBO;
        private int _sphereIndexCount;

        private uint _locationVAO;
        private uint _locationVBO;
        private int _locationCount;

        private uint _mapVAO;
        private uint _mapVBO;
        private int _mapVertexCount;

        private uint _earthTexture;

        private Matrix4x4 _viewMatrix;
        private Matrix4x4 _projectionMatrix;
        private Vector3 _cameraPosition = new Vector3(0, 0, 5);
        private Vector3 _sunDirection = Vector3.Normalize(new Vector3(1, 0, 1));
        private float _rotationAngle = 0.0f;

        public OpenGLRenderer(IWindow window)
        {
            _window = window;
        }

        public void Initialize()
        {
            _gl = GL.GetApi(_window);
            _gl.Enable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.ProgramPointSize); // Чтобы работал gl_PointSize в шейдере звезд
            _gl.Disable(EnableCap.CullFace); // Отключаем culling, если он вызывает проблемы с отображением

            _earthShaderProgram = CreateShaderProgram(LoadShader("Shaders/earth.vert"), LoadShader("Shaders/earth.frag"));
            _earthViewLoc = _gl.GetUniformLocation(_earthShaderProgram, "view");
            _earthProjLoc = _gl.GetUniformLocation(_earthShaderProgram, "projection");
            _earthModelLoc = _gl.GetUniformLocation(_earthShaderProgram, "model");
            _earthTexLoc = _gl.GetUniformLocation(_earthShaderProgram, "earthTexture");
            _earthSunLoc = _gl.GetUniformLocation(_earthShaderProgram, "sunDirection");

            _atmosphereShaderProgram = CreateShaderProgram(LoadShader("Shaders/atmosphere.vert"), LoadShader("Shaders/atmosphere.frag"));
            _atmoViewLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "view");
            _atmoProjLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "projection");
            _atmoModelLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "model");
            _atmoSunLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "sunDirection");
            _atmoCamLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "cameraPosition");
            _atmoEarthRadiusLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "earthRadius");
            _atmoAtmosphereRadiusLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "atmosphereRadius");
            _atmoRayleighCoeffLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "rayleighCoeff");
            _atmoMieCoeffLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "mieCoeff");
            _atmoMieGLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "mieG");
            _atmoRayleighScaleHeightLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "rayleighScaleHeight");
            _atmoMieScaleHeightLoc = _gl.GetUniformLocation(_atmosphereShaderProgram, "mieScaleHeight");

            _starShaderProgram = CreateShaderProgram(LoadShader("Shaders/stars.vert"), LoadShader("Shaders/stars.frag"));
            _starViewLoc = _gl.GetUniformLocation(_starShaderProgram, "view");
            _starProjLoc = _gl.GetUniformLocation(_starShaderProgram, "projection");

            _locationShaderProgram = CreateShaderProgram(LoadShader("Shaders/location.vert"), LoadShader("Shaders/location.frag"));
            _locationViewLoc = _gl.GetUniformLocation(_locationShaderProgram, "view");
            _locationProjLoc = _gl.GetUniformLocation(_locationShaderProgram, "projection");
            _locationDistanceLoc = _gl.GetUniformLocation(_locationShaderProgram, "cameraDistance");

            _mapShaderProgram = CreateShaderProgram(LoadShader("Shaders/map.vert"), LoadShader("Shaders/map.frag"));
            _mapViewLoc = _gl.GetUniformLocation(_mapShaderProgram, "view");
            _mapProjLoc = _gl.GetUniformLocation(_mapShaderProgram, "projection");
            _mapModelLoc = _gl.GetUniformLocation(_mapShaderProgram, "model");
            _mapColorLoc = _gl.GetUniformLocation(_mapShaderProgram, "color");

            CreateSphere(1.0f, 64);
            CreateStars(_starCount);
            CreateLocations();
            CreateMapFromGeoJson("Resources/countries_3d.geojson");
            //_earthTexture = LoadTexture("Resources/worldmap.png");
        }

        public void Render()
        {
            _gl.Viewport(0, 0, (uint)_window.Size.X, (uint)_window.Size.Y);
            _gl.ClearColor(0.02f, 0.02f, 0.05f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            float aspectRatio = (float)_window.Size.X / Math.Max(_window.Size.Y, 1);
            // Уменьшаем FOV для уменьшения перспективных искажений (как в Earth3D)
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 6.0f, aspectRatio, 0.1f, 200.0f);
            _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, Vector3.Zero, Vector3.UnitY);

            RenderStars();
            RenderEarth();
            RenderMap();
            RenderLocations();

            // Рендерим атмосферу с прозрачностью
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            _gl.DepthMask(false); // Атмосфера не должна перекрывать объекты за собой в буфере глубины

            RenderAtmosphere();

            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        private unsafe void RenderStars()
        {
            _gl.UseProgram(_starShaderProgram);

            fixed (Matrix4x4* viewPtr = &_viewMatrix)
                _gl.UniformMatrix4(_starViewLoc, 1, false, (float*)viewPtr);
            fixed (Matrix4x4* projPtr = &_projectionMatrix)
                _gl.UniformMatrix4(_starProjLoc, 1, false, (float*)projPtr);

            _gl.BindVertexArray(_starVAO);
            _gl.DrawArrays(PrimitiveType.Points, 0, (uint)_starCount);
        }

        private unsafe void RenderEarth()
        {
            _gl.UseProgram(_earthShaderProgram);

            fixed (Matrix4x4* viewPtr = &_viewMatrix)
                _gl.UniformMatrix4(_earthViewLoc, 1, false, (float*)viewPtr);

            fixed (Matrix4x4* projPtr = &_projectionMatrix)
                _gl.UniformMatrix4(_earthProjLoc, 1, false, (float*)projPtr);

            Matrix4x4 model = Matrix4x4.Identity;
            _gl.UniformMatrix4(_earthModelLoc, 1, false, (float*)&model);

            //_gl.Uniform1(_earthTexLoc, 0);
            //_gl.Uniform3(_earthSunLoc, _sunDirection.X, _sunDirection.Y, _sunDirection.Z);
            //_gl.ActiveTexture(TextureUnit.Texture0);
            //_gl.BindTexture(TextureTarget.Texture2D, _earthTexture);

            _gl.BindVertexArray(_sphereVAO);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_sphereIndexCount, DrawElementsType.UnsignedInt, null);
        }

        private unsafe void RenderMap()
        {
            if (_mapVertexCount == 0) return;

            _gl.UseProgram(_mapShaderProgram);

            fixed (Matrix4x4* viewPtr = &_viewMatrix)
                _gl.UniformMatrix4(_mapViewLoc, 1, false, (float*)viewPtr);

            fixed (Matrix4x4* projPtr = &_projectionMatrix)
                _gl.UniformMatrix4(_mapProjLoc, 1, false, (float*)projPtr);

            Matrix4x4 model = Matrix4x4.Identity;
            _gl.UniformMatrix4(_mapModelLoc, 1, false, (float*)&model);

            // Рисуем границы стран (белым цветом)
            _gl.Uniform3(_mapColorLoc, 1.0f, 1.0f, 1.0f);
            _gl.BindVertexArray(_mapVAO);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_mapVertexCount);
        }

        private unsafe void RenderAtmosphere()
        {
            _gl.UseProgram(_atmosphereShaderProgram);

            fixed (Matrix4x4* viewPtr = &_viewMatrix)
                _gl.UniformMatrix4(_atmoViewLoc, 1, false, (float*)viewPtr);

            fixed (Matrix4x4* projPtr = &_projectionMatrix)
                _gl.UniformMatrix4(_atmoProjLoc, 1, false, (float*)projPtr);

            Matrix4x4 model = Matrix4x4.CreateScale(1.1f);
            _gl.UniformMatrix4(_atmoModelLoc, 1, false, (float*)&model);

            _gl.Uniform3(_atmoSunLoc, _sunDirection.X, _sunDirection.Y, _sunDirection.Z);
            _gl.Uniform3(_atmoCamLoc, _cameraPosition.X, _cameraPosition.Y, _cameraPosition.Z);
            _gl.Uniform1(_atmoEarthRadiusLoc, 1.0f);
            _gl.Uniform1(_atmoAtmosphereRadiusLoc, 1.05f);
            _gl.Uniform3(_atmoRayleighCoeffLoc, 1.8e-3f, 13.5e-3f, 33.1e-3f);
            _gl.Uniform1(_atmoMieCoeffLoc, 21e-3f);
            _gl.Uniform1(_atmoMieGLoc, 0.758f);
            _gl.Uniform1(_atmoRayleighScaleHeightLoc, 0.08f);
            _gl.Uniform1(_atmoMieScaleHeightLoc, 0.012f);

            _gl.BindVertexArray(_sphereVAO);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_sphereIndexCount, DrawElementsType.UnsignedInt, null);
        }

        private unsafe void CreateSphere(float radius, int segments)
        {
            var vertices = new System.Collections.Generic.List<float>();
            var indices = new System.Collections.Generic.List<uint>();

            for (int i = 0; i <= segments; i++)
            {
                float v = (float)i / segments;
                float phi = MathF.PI * i / segments;

                for (int j = 0; j <= segments; j++)
                {
                    // Сдвиг на 0.5 для идентичности с Earth3D
                    float u = (float)j / segments + 0.5f;
                    float theta = 2 * MathF.PI * j / segments;

                    // Инвертируем X (умножаем на -1), как это делает ScaleTransform3D(-1, 1, 1) в Earth3D
                    float x = -radius * MathF.Sin(phi) * MathF.Cos(theta);
                    float y = radius * MathF.Cos(phi);
                    float z = radius * MathF.Sin(phi) * MathF.Sin(theta);

                    vertices.Add(x);
                    vertices.Add(y);
                    vertices.Add(z);
                    vertices.Add(u);
                    vertices.Add(v);
                }
            }

            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < segments; j++)
                {
                    uint first = (uint)(i * (segments + 1) + j);
                    uint second = first + (uint)segments + 1;

                    // Порядок индексов как в Earth3D
                    indices.Add(first);
                    indices.Add(first + 1);
                    indices.Add(second);

                    indices.Add(second);
                    indices.Add(first + 1);
                    indices.Add(second + 1);
                }
            }

            _sphereVAO = _gl.GenVertexArray();
            _gl.BindVertexArray(_sphereVAO);

            _sphereVBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sphereVBO);
            var vertArray = vertices.ToArray();
            fixed (float* ptr = vertArray)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertArray.Length * sizeof(float)), ptr,
                    BufferUsageARB.StaticDraw);

            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            _gl.EnableVertexAttribArray(0);

            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
            _gl.EnableVertexAttribArray(1);

            _sphereEBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _sphereEBO);
            var indArray = indices.ToArray();
            fixed (uint* ptr = indArray)
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indArray.Length * sizeof(uint)), ptr,
                    BufferUsageARB.StaticDraw);

            _sphereIndexCount = indices.Count;
        }


        private void CreateStars(int count)
        {
            var starPositions = new float[count * 3];
            Random rand = new Random();
            for (int i = 0; i < count; i++)
            {
                float theta = (float)(rand.NextDouble() * 2.0 * Math.PI);
                float phi = (float)(Math.Acos(2.0 * rand.NextDouble() - 1.0));
                float r = 100.0f;

                starPositions[i * 3] = r * MathF.Sin(phi) * MathF.Cos(theta);
                starPositions[i * 3 + 1] = r * MathF.Cos(phi);
                starPositions[i * 3 + 2] = r * MathF.Sin(phi) * MathF.Sin(theta);
            }

            _starVAO = _gl.GenVertexArray();
            _gl.BindVertexArray(_starVAO);
            _starVBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _starVBO);
            unsafe
            {
                fixed (float* ptr = starPositions)
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(starPositions.Length * sizeof(float)), ptr,
                        BufferUsageARB.StaticDraw);
            }

            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            _gl.EnableVertexAttribArray(0);
        }

        private unsafe void CreateMapFromGeoJson(string path)
        {
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            var collection = JsonSerializer.Deserialize<GeoJsonFeatureCollection>(json);
            if (collection == null) return;

            var vertices = new System.Collections.Generic.List<float>();

            foreach (var feature in collection.Features)
            {
                if (feature.Geometry == null) continue;

                if (feature.Geometry.Type == "Polygon")
                {
                    var coords = feature.Geometry.Coordinates.Deserialize<List<List<List<double>>>>();
                    if (coords != null) AddPolygonVertices(coords, vertices);
                }
                else if (feature.Geometry.Type == "MultiPolygon")
                {
                    var coords = feature.Geometry.Coordinates.Deserialize<List<List<List<List<double>>>>>();
                    if (coords != null)
                    {
                        foreach (var polygon in coords)
                        {
                            AddPolygonVertices(polygon, vertices);
                        }
                    }
                }
            }

            _mapVertexCount = vertices.Count / 3;
            if (_mapVertexCount == 0) return;

            _mapVAO = _gl.GenVertexArray();
            _gl.BindVertexArray(_mapVAO);

            _mapVBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _mapVBO);
            var vertArray = vertices.ToArray();
            fixed (float* ptr = vertArray)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertArray.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);

            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            _gl.EnableVertexAttribArray(0);
        }

        private void AddPolygonVertices(List<List<List<double>>> polygon, System.Collections.Generic.List<float> vertices)
        {
            foreach (var ring in polygon)
            {
                for (int i = 0; i < ring.Count - 1; i++)
                {
                    AddVertex(ring[i][0], ring[i][1], vertices);
                    AddVertex(ring[i + 1][0], ring[i + 1][1], vertices);
                }
            }
        }

        private void AddVertex(double lon, double lat, System.Collections.Generic.List<float> vertices)
        {
            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            float radius = 1.001f; // Чуть выше поверхности сферы
            // Используем ту же логику трансформации, что и в CreateSphere
            float x = -radius * MathF.Sin((float)(Math.PI / 2 - latRad)) * MathF.Cos((float)lonRad);
            float y = radius * MathF.Cos((float)(Math.PI / 2 - latRad));
            float z = radius * MathF.Sin((float)(Math.PI / 2 - latRad)) * MathF.Sin((float)lonRad);

            vertices.Add(x);
            vertices.Add(y);
            vertices.Add(z);
        }

        private void CreateLocations()
        {
            // Initially empty, will be populated when location is added
            _locationCount = 0;
        }

        private unsafe void RenderLocations()
        {
            if (_locationCount == 0) return;

            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _gl.UseProgram(_locationShaderProgram);

            fixed (Matrix4x4* viewPtr = &_viewMatrix)
                _gl.UniformMatrix4(_locationViewLoc, 1, false, (float*)viewPtr);
            fixed (Matrix4x4* projPtr = &_projectionMatrix)
                _gl.UniformMatrix4(_locationProjLoc, 1, false, (float*)projPtr);

            float cameraDistance = _cameraPosition.Length();
            _gl.Uniform1(_locationDistanceLoc, cameraDistance);

            _gl.BindVertexArray(_locationVAO);
            _gl.DrawArrays(PrimitiveType.Points, 0, (uint)_locationCount);

            _gl.Disable(EnableCap.Blend);
        }

        private unsafe uint LoadTexture(string path)
        {
            uint texture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, texture);

            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            fixed (byte* ptr = image.Data)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                    (uint)image.Width, (uint)image.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }

            _gl.GenerateMipmap(TextureTarget.Texture2D);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            // Включаем анизотропную фильтрацию для максимальной четкости под углом
            if (_gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic"))
            {
                float maxAnisotropy = 0;
                _gl.GetFloat((GLEnum)0x84FF, out maxAnisotropy); // 0x84FF = GL_MAX_TEXTURE_MAX_ANISOTROPY_EXT
                if (maxAnisotropy > 0)
                {
                    _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, maxAnisotropy); // 0x84FE = GL_TEXTURE_MAX_ANISOTROPY_EXT
                }
            }

            return texture;
        }

        private static string LoadShader(string name) => File.ReadAllText(name);

        private uint CreateShaderProgram(string vertexSource, string fragmentSource)
        {
            uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(vertexShader, vertexSource);
            _gl.CompileShader(vertexShader);

            string vLog = _gl.GetShaderInfoLog(vertexShader);
            if (!string.IsNullOrWhiteSpace(vLog))
                throw new Exception($"Error compiling vertex shader: {vLog}");

            uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(fragmentShader, fragmentSource);
            Console.WriteLine("Compiling fragment shader:");
            Console.WriteLine(fragmentSource);
            _gl.CompileShader(fragmentShader);

            string fLog = _gl.GetShaderInfoLog(fragmentShader);
            if (!string.IsNullOrWhiteSpace(fLog))
                throw new Exception($"Error compiling fragment shader: {fLog}");

            uint program = _gl.CreateProgram();
            _gl.AttachShader(program, vertexShader);
            _gl.AttachShader(program, fragmentShader);
            _gl.LinkProgram(program);

            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0)
                throw new Exception($"Error linking shader program: {_gl.GetProgramInfoLog(program)}");

            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);

            return program;
        }

        public void AddLocationPoint(double lat, double lon)
        {
            // Convert lat/lon to 3D coordinates
            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            float radius = 1.01f; // Slightly above surface
            float x = -radius * MathF.Sin((float)(Math.PI / 2 - latRad)) * MathF.Cos((float)lonRad);
            float y = radius * MathF.Cos((float)(Math.PI / 2 - latRad));
            float z = radius * MathF.Sin((float)(Math.PI / 2 - latRad)) * MathF.Sin((float)lonRad);

            // For now, recreate the entire buffer with one point
            // In a real implementation, you'd use a dynamic list and update the buffer
            var positions = new float[] { x, y, z };

            _locationVAO = _gl.GenVertexArray();
            _gl.BindVertexArray(_locationVAO);
            _locationVBO = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _locationVBO);
            unsafe
            {
                fixed (float* ptr = positions)
                    _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(positions.Length * sizeof(float)), ptr,
                        BufferUsageARB.StaticDraw);
            }

            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            _gl.EnableVertexAttribArray(0);
            _locationCount = 1;
        }

        public void UpdateCamera(Vector3 position) => _cameraPosition = position;
        public Vector3 GetCameraPosition() => _cameraPosition;
        public void UpdateSunDirection(Vector3 direction) => _sunDirection = Vector3.Normalize(direction);
    }
}
