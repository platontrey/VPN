# AGENTS.md

This file contains guidelines and commands for agentic coding agents working in the HysteryVPN repository.

## Project Overview

HysteryVPN is a C# WPF application that provides VPN functionality with 3D globe visualization using OpenGL. The project uses .NET 9.0-windows target framework and follows MVVM architecture pattern.

## Build Commands

### Basic Build
```bash
dotnet build
```

### Build Specific Configuration
```bash
dotnet build -c Release    # Release build
dotnet build -c Debug      # Debug build
```

### Build Solution
```bash
dotnet build HysteryVPN.sln
```

### Run Application
```bash
dotnet run --project HysteryVPN
```

### Clean Build
```bash
dotnet clean
dotnet build
```

### Test Commands
No test framework is currently configured in this project. To add tests:
1. Create a test project (xUnit, NUnit, or MSTest)
2. Add test project to solution
3. Configure test runner commands here

## Code Style Guidelines

### General Structure
- Follow MVVM pattern: Views (XAML), ViewModels, Models, Services
- Use namespace organization: `HysteryVPN.{Subfolder}`
- Keep files focused on single responsibility

### Imports and Using Statements
- System imports first, grouped alphabetically
- Third-party imports second (CommunityToolkit, HelixToolkit, Silk.NET)
- Project imports last, grouped by namespace
- Remove unused using statements
- Example:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.Wpf;
using Silk.NET.OpenGL;

using HysteryVPN.Services;
using HysteryVPN.Models;
using HysteryVPN.Rendering;
```

### Naming Conventions
- **Classes**: PascalCase (e.g., `VpnManager`, `MainViewModel`)
- **Methods**: PascalCase (e.g., `StartVpnAsync`, `UpdateUI`)
- **Properties**: PascalCase (e.g., `StatusText`, `IsConnected`)
- **Fields**: camelCase with underscore prefix for private fields (e.g., `_logger`, `_isConnected`)
- **Constants**: PascalCase (e.g., `DWMWA_USE_IMMERSIVE_DARK_MODE`)
- **Enums**: PascalCase (e.g., `QualityLevel`)

### Async/Await Patterns
- Use `async Task` for void-returning async methods
- Use `async Task<T>` for methods returning values
- Configure `ConfigureAwait(false)` in library code, not UI code
- Handle exceptions properly with try-catch blocks
- Example:
```csharp
public async Task StartVpnAsync(string link)
{
    try
    {
        await _vpnManager.ConnectAsync(link);
        await UpdateUI();
    }
    catch (Exception ex)
    {
        _logger.LogError($"Failed to start VPN: {ex.Message}");
    }
}
```

### Error Handling
- Use try-catch blocks for operations that can throw exceptions
- Log errors using the Logger service
- Provide meaningful error messages to users
- Use specific exception types when possible
- Example:
```csharp
try
{
    var response = await client.GetStringAsync(url);
    return response;
}
catch (HttpRequestException ex)
{
    _logger.LogError($"HTTP request failed: {ex.Message}");
    throw;
}
catch (TaskCanceledException ex)
{
    _logger.LogError($"Request timeout: {ex.Message}");
    throw;
}
```

### Dependency Injection
- Constructor injection is preferred
- Register services in ViewModels
- Keep services stateless when possible
- Example:
```csharp
public MainViewModel()
{
    _logger = new Logger(s => LogText += s, Dispatcher.CurrentDispatcher);
    _settingsManager = new SettingsManager(_logger);
    _vpnManager = new VpnManager(_logger, _configGenerator, _routeManager);
}
```

### WPF Specific Guidelines
- Use MVVM with CommunityToolkit.Mvvm
- Properties with `[ObservableProperty]` attribute
- Commands with `[RelayCommand]` attribute
- Use partial methods for property change handlers
- Example:
```csharp
[ObservableProperty]
private string statusText = "Not connected";

partial void OnStatusTextChanged(string value)
{
    // Handle property change
}

[RelayCommand]
private async Task ConnectAsync()
{
    // Command implementation
}
```

### OpenGL and Rendering
- Use Silk.NET for OpenGL bindings
- Separate rendering logic into dedicated classes
- Cache uniform locations for performance
- Dispose OpenGL resources properly
- Use vertex buffer objects (VBOs) and vertex array objects (VAOs)

### File Organization
- **Models/**: Data models and DTOs
- **ViewModels/**: MVVM view models
- **Views/**: XAML views and windows
- **Services/**: Business logic and external integrations
- **Rendering/**: OpenGL and graphics-related code
- **Resources/**: Static resources (images, geojson files)
- **Shaders/**: GLSL shader files

### Resource Management
- Implement IDisposable for classes managing unmanaged resources
- Use `using` statements for disposable objects
- Properly clean up OpenGL resources (buffers, shaders, textures)
- Example:
```csharp
public class OpenGLRenderer : IDisposable
{
    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
            }
            // Dispose unmanaged resources
            _disposed = true;
        }
    }
}
```

### Performance Considerations
- Use object pooling for frequently allocated objects
- Minimize UI thread blocking with async operations
- Cache expensive computations
- Use weak references for event handlers to prevent memory leaks
- Optimize OpenGL rendering with VBOs and batched draws

### Security
- Never log sensitive information (passwords, tokens, private keys)
- Validate external inputs
- Use HTTPS for network requests
- Securely store configuration files

### Comments and Documentation
- Use XML documentation for public APIs
- Add comments for complex business logic
- Document shader uniforms and vertex attributes
- Keep comments concise and up-to-date

## Development Workflow

1. **Before making changes**: Run `dotnet build` to ensure project compiles
2. **After changes**: Test the application functionality
3. **Code review**: Ensure adherence to style guidelines
4. **Final build**: Run Release build to verify optimization

## External Dependencies

- **CommunityToolkit.Mvvm**: MVVM framework
- **HelixToolkit.Wpf**: 3D visualization helpers
- **Silk.NET**: OpenGL and windowing bindings
- **StbImageSharp**: Image loading for textures

## Platform Requirements

- Windows OS (WPF requirement)
- .NET 9.0-windows runtime
- OpenGL 3.3+ capable GPU for 3D visualization
- Administrator privileges may be required for VPN functionality