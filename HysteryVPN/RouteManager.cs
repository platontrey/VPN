using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace HysteryVPN
{
    public class RouteManager
    {
        private readonly Logger _logger;

        public RouteManager(Logger logger)
        {
            _logger = logger;
        }

        public async Task AddBypassRouteAsync(string ip)
        {
            try
            {
                // Используем PowerShell чтобы автоматически найти шлюз и добавить маршрут
                string cmd = $"$g = (Get-NetRoute -DestinationPrefix 0.0.0.0/0).NextHop; route add {ip} mask 255.255.255.255 $g metric 1";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{cmd}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(startInfo);
                if (p != null)
                {
                    await p.WaitForExitAsync();
                }
                _logger.Log("Bypass route added successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to add bypass route: {ex.Message}");
            }
        }

        public void RemoveBypassRoute(string ip)
        {
            try
            {
                string cmd = $"route delete {ip}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {cmd}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(startInfo)?.WaitForExit();
                _logger.Log("Bypass route removed.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to remove bypass route: {ex.Message}");
            }
        }

        public async Task<List<string>> AddBypassRouteForDomainAsync(string domain)
        {
            var addedIps = new List<string>();
            try
            {
                var addresses = Dns.GetHostAddresses(domain);
                foreach (var addr in addresses)
                {
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // IPv4 only
                    {
                        string ip = addr.ToString();
                        await AddBypassRouteAsync(ip);
                        addedIps.Add(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to resolve {domain}: {ex.Message}");
            }
            return addedIps;
        }
    }
}