using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using HysteryVPN.Services;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace HysteryVPN.Rendering
{
    public class OpenGLControl : Control
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public event Action<float, float>? CameraRotated;

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private const uint SWP_SHOWWINDOW = 0x0040;

        private IWindow? _window;
        private OpenGLRenderer? _renderer;
        private bool _isInitialized;
        private float _renderScale = 1.0f;

        private System.Drawing.Point _lastGlobalMousePos;
        private float _yaw;
        private float _pitch;
        private float _radius = 5.0f;
        private float _targetYaw;
        private float _targetPitch;
        private float _targetRadius = 5.0f;
        private bool _wasMouseDown;

        public OpenGLControl()
        {
            DoubleBuffered = false;
            BackColor = System.Drawing.Color.Black;

            // Нужно, чтобы WinForms контрол внутри WindowsFormsHost гарантированно получал WM_MOUSEWHEEL
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            MouseEnter += (_, _) => Focus();
            MouseDown += (_, _) => Focus();
        }

        private void InitializeOpenGL()
        {
            if (_isInitialized) return;

            var options = WindowOptions.Default;
            options.Size = new Vector2D<int>(
                (int)(Math.Max(Width, 100) * _renderScale),
                (int)(Math.Max(Height, 100) * _renderScale));
            options.WindowBorder = WindowBorder.Hidden;
            options.WindowState = WindowState.Normal;
            options.IsVisible = true;
            options.API = GraphicsAPI.Default;
            options.Samples = 4;

            _window = Window.Create(options);
            _renderer = new OpenGLRenderer(_window);

            _window.Load += async () =>
            {
                if (_window.Native.Win32.HasValue)
                {
                    IntPtr silkHwnd = _window.Native.Win32.Value.Hwnd;
                    SetParent(silkHwnd, Handle);
                    SetWindowLong(silkHwnd, GWL_STYLE, WS_CHILD | WS_VISIBLE);
                    UpdateChildWindowSize();
                }

                _renderer.Initialize();

                // Get user location and add point
                var location = await GeoLocationService.GetLocationByIPAsync();
                if (location.HasValue)
                {
                    AddLocationPoint(location.Value.lat, location.Value.lon);
                }
            };

            _window.Render += delta =>
            {
                HandleGlobalMouse();

                float lerpFactor = 1.0f - MathF.Pow(0.001f, (float)delta);
                _yaw += (_targetYaw - _yaw) * lerpFactor;
                _pitch += (_targetPitch - _pitch) * lerpFactor;
                _radius += (_targetRadius - _radius) * lerpFactor;

                float x = _radius * MathF.Cos(_yaw) * MathF.Cos(_pitch);
                float y = _radius * MathF.Sin(_pitch);
                float z = _radius * MathF.Sin(_yaw) * MathF.Cos(_pitch);

                _renderer.UpdateCamera(new System.Numerics.Vector3(x, y, z));

                // Обновляем направление солнца на основе реального времени UTC
                DateTime now = DateTime.UtcNow;
                double hours = now.Hour + now.Minute / 60.0 + now.Second / 3600.0;
                
                // Угол в радианах. 12:00 UTC -> 0 rad, 00:00 UTC -> PI rad
                // Земля вращается с запада на восток, солнце "движется" с востока на запад.
                double longitudeRad = (hours - 12.0) * (2.0 * Math.PI / 24.0);
                
                // Учитываем наклон земной оси (примерно 23.44 градуса)
                int dayOfYear = now.DayOfYear;
                double declination = 23.44 * Math.Sin(2.0 * Math.PI * (dayOfYear - 81) / 365.0) * Math.PI / 180.0;

                // Вычисляем вектор направления солнца (инвертированный, так как это направление К объекту)
                float sunX = (float)(Math.Cos(declination) * Math.Cos(longitudeRad));
                float sunY = (float)Math.Sin(declination);
                float sunZ = (float)(Math.Cos(declination) * Math.Sin(longitudeRad));

                var sunDir = new System.Numerics.Vector3(-sunX, -sunY, -sunZ);
                _renderer.UpdateSunDirection(sunDir);

                _renderer.Render();
            };

            _window.Initialize();
            _isInitialized = true;
        }

        private void HandleGlobalMouse()
        {
            bool isDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            GetCursorPos(out var currentPos);

            var localPos = PointToClient(currentPos);
            bool isOverControl = ClientRectangle.Contains(localPos);

            if (isDown && (isOverControl || _wasMouseDown))
            {
                if (_wasMouseDown)
                {
                    float deltaX = currentPos.X - _lastGlobalMousePos.X;
                    float deltaY = currentPos.Y - _lastGlobalMousePos.Y;

                    _targetYaw += deltaX * 0.005f;
                    _targetPitch -= deltaY * 0.005f;
                    _targetPitch = Math.Clamp(_targetPitch, -1.5f, 1.5f);

                    CameraRotated?.Invoke(_targetYaw, _targetPitch);
                }

                _wasMouseDown = true;
            }
            else
            {
                _wasMouseDown = false;
            }

            _lastGlobalMousePos = currentPos;
        }

        private void UpdateChildWindowSize()
        {
            if (_window != null && _window.Native.Win32.HasValue)
            {
                IntPtr hwnd = _window.Native.Win32.Value.Hwnd;
                SetWindowPos(hwnd, HWND_TOP, 0, 0, Width, Height, SWP_SHOWWINDOW);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            InitializeOpenGL();
        }

        public void UpdateCamera(System.Numerics.Vector3 position)
        {
            // Всегда обновляем целевой радиус, чтобы Zoom работал
            _targetRadius = position.Length();

            // Обновляем углы только если пользователь не вращает глобус мышью
            if (!_wasMouseDown)
            {
                _targetYaw = MathF.Atan2(position.Z, position.X);
                _targetPitch = MathF.Asin(position.Y / Math.Max(_targetRadius, 0.1f));

                // Если это первый запуск, синхронизируем мгновенно
                if (_yaw == 0 && _pitch == 0)
                {
                    _yaw = _targetYaw;
                    _pitch = _targetPitch;
                    _radius = _targetRadius;
                }
            }

            Invalidate();
        }

        public void UpdateSunDirection(System.Numerics.Vector3 direction)
        {
            _renderer?.UpdateSunDirection(direction);
            Invalidate();
        }

        public void AddLocationPoint(double lat, double lon)
        {
            _renderer?.AddLocationPoint(lat, lon);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_isInitialized || _window == null)
            {
                e.Graphics.Clear(System.Drawing.Color.Black);
                return;
            }

            _window.DoRender();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (_isInitialized && _window != null)
            {
                _window.Size = new Vector2D<int>(
                    (int)(Math.Max(Width, 1) * _renderScale),
                    (int)(Math.Max(Height, 1) * _renderScale));
                UpdateChildWindowSize();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _window?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
    
