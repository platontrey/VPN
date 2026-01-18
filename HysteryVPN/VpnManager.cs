using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HysteryVPN
{
    public class VpnManager
    {
        private Process? _coreProcess;
        private Process? _zapretProcess;
        private bool _isConnected = false;
        private string _serverIp = "";
        private readonly Logger _logger;
        private readonly ConfigGenerator _configGenerator;
        private readonly RouteManager _routeManager;
        private readonly string[] _zapretDomains = { "youtube.com", "discord.com", "roblox.com", "2ip.ru", "browserleaks.com" };
        private List<string> _bypassIps = new List<string>();

        public VpnManager(Logger logger, ConfigGenerator configGenerator, RouteManager routeManager)
        {
            _logger = logger;
            _configGenerator = configGenerator;
            _routeManager = routeManager;
        }

        public bool IsConnected => _isConnected;
        public string ServerIp => _serverIp;

        public async Task StartVpnAsync(string link, bool enableTurn, bool enableZapret)
        {
            try
            {
                if (string.IsNullOrEmpty(link))
                {
                    _logger.LogError("Link is empty!");
                    return;
                }

                if (!File.Exists("core.exe") || !File.Exists("wintun.dll"))
                    throw new Exception("Files missing: core.exe or wintun.dll not found!");

                _logger.Log("Configuring...");
                string serverIp;
                string[] bypassDomains = _zapretDomains; // Always bypass these domains
                _configGenerator.GenerateConfig(link, enableTurn, bypassDomains, out serverIp);
                _serverIp = serverIp;

                // Добавляем bypass маршрут
                if (!string.IsNullOrEmpty(serverIp))
                {
                    _logger.Log($"Adding bypass route for server: {serverIp}");
                    await _routeManager.AddBypassRouteAsync(serverIp);
                }

                _logger.Log("Starting Core (IPv4 Only Mode)...");

                var psi = new ProcessStartInfo
                {
                    FileName = "core.exe",
                    Arguments = $"-c config.json client",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                _coreProcess = new Process { StartInfo = psi };
                _coreProcess.OutputDataReceived += (s, args) => _logger.LogCore(args.Data ?? "");
                _coreProcess.ErrorDataReceived += (s, args) => _logger.LogCore(args.Data ?? "");

                // Run process start and zapret in background to avoid blocking UI
                await Task.Run(() =>
                {
                    if (enableZapret)
                    {
                        StartZapret();
                    }

                    _coreProcess.Start();
                    _coreProcess.BeginOutputReadLine();
                    _coreProcess.BeginErrorReadLine();
                });

                _isConnected = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"CRITICAL ERROR: {ex.Message}");
                StopVpn();
            }
        }

        public void StopVpn()
        {
            if (_coreProcess != null && !_coreProcess.HasExited)
            {
                try { _coreProcess.Kill(); } catch { }
                _coreProcess = null;
            }

            StopZapret();

            _isConnected = false;
            _serverIp = "";
            _logger.Log("Disconnected.");
        }

        private async Task LogIpv6StatusAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "interface ipv6 show interfaces",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    string output = await p.StandardOutput.ReadToEndAsync();
                    string error = await p.StandardError.ReadToEndAsync();
                    await Task.Run(() => p!.WaitForExit());
                    _logger.Log("IPv6 Interfaces:");
                    _logger.Log(output);
                    if (!string.IsNullOrEmpty(error)) _logger.Log("IPv6 Error: " + error);
                }
            }
            catch (Exception ex)
            {
                _logger.Log("Failed to check IPv6: " + ex.Message);
            }
        }

        private void StartZapret()
        {
            try
            {
                if (!File.Exists("winws.exe"))
                {
                    _logger.LogWarning("winws.exe not found, skipping Zapret");
                    return;
                }

                // Create hostlist file
                string hostlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zapret_hostlist.txt");
                File.WriteAllLines(hostlistFile, _zapretDomains);

                _logger.Log("Starting Zapret for DPI bypass...");
                var psi = new ProcessStartInfo
                {
                    FileName = "winws.exe",
                    Arguments = $"--wf-tcp=80,443 --hostlist=\"{hostlistFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                _zapretProcess = new Process { StartInfo = psi };
                _zapretProcess.OutputDataReceived += (s, args) => _logger.LogCore("Zapret: " + (args.Data ?? ""));
                _zapretProcess.ErrorDataReceived += (s, args) => _logger.LogCore("Zapret: " + (args.Data ?? ""));

                _zapretProcess.Start();
                _zapretProcess.BeginOutputReadLine();
                _zapretProcess.BeginErrorReadLine();

                _logger.Log("Zapret started.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start Zapret: {ex.Message}");
            }
        }

        private void StopZapret()
        {
            if (_zapretProcess != null && !_zapretProcess.HasExited)
            {
                try { _zapretProcess.Kill(); } catch { }
                _zapretProcess = null;
                _logger.Log("Zapret stopped.");
            }

            // Clean up hostlist file
            string hostlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zapret_hostlist.txt");
            if (File.Exists(hostlistFile))
            {
                try { File.Delete(hostlistFile); } catch { }
            }
        }
    }
}