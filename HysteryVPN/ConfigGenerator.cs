using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;

namespace HysteryVPN
{
    public class ConfigGenerator
    {
        private const string ConfigFile = "config.json";
        private readonly Logger _logger;

        public ConfigGenerator(Logger logger)
        {
            _logger = logger;
        }

        public string GenerateConfig(string rawUri, bool enableTurn, string[] bypassDomains, out string serverIp)
        {
            serverIp = "";
            try
            {
                if (!rawUri.StartsWith("hy2://")) throw new Exception("Link must start with hy2://");
                var uri = new Uri(rawUri);
                serverIp = uri.Host;

                // Парсинг параметров
                var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in uri.Query.TrimStart('?').Split('&'))
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2) p[kv[0]] = Uri.UnescapeDataString(kv[1]);
                }
                string Get(string k) => p.ContainsKey(k) ? p[k] : "";

                // Auth & Obfs
                string auth = uri.UserInfo;
                object? obfsObj = null;
                string obfsType = Get("obfs").ToLower();
                if (!string.IsNullOrEmpty(obfsType) && obfsType != "none")
                {
                    if (obfsType == "salamander")
                        obfsObj = new { type = "salamander", salamander = new { password = Get("obfs-password") } };
                    else
                        obfsObj = new { type = obfsType };
                }

                // TURN Relay
                object? relayObj = null;
                string turnAddress = Get("turn");
                if (enableTurn && !string.IsNullOrEmpty(turnAddress))
                {
                    relayObj = new
                    {
                        address = turnAddress,
                        username = Get("turn-user"),
                        password = Get("turn-pass")
                    };
                }

                object? routeObj = null;
                if (bypassDomains != null && bypassDomains.Length > 0)
                {
                    routeObj = new
                    {
                        rules = new[]
                        {
                            new
                            {
                                action = "bypass",
                                domain = bypassDomains
                            }
                        }
                    };
                }

                object dnsObj = new { server = "8.8.8.8" };
                if (bypassDomains != null && bypassDomains.Length > 0)
                {
                    dnsObj = new
                    {
                        server = "8.8.8.8",
                        rules = new[]
                        {
                            new
                            {
                                domain = bypassDomains,
                                server = "1.1.1.1"
                            }
                        }
                    };
                }

                var config = new
                {
                    server = $"{uri.Host}:{uri.Port}",
                    auth = auth,
                    tls = new
                    {
                        sni = string.IsNullOrEmpty(Get("sni")) ? "github.com" : Get("sni"),
                        insecure = Get("insecure") == "1",
                        pinSHA256 = string.IsNullOrEmpty(Get("pinSHA256")) ? null : Get("pinSHA256")
                    },
                    obfs = obfsObj,
                    relay = relayObj,
                    bandwidth = new { up = "50 mbps", down = "100 mbps" },
                    dns = dnsObj,
                    route = routeObj,
                    tun = new
                    {
                        name = "hysteria-tun0",
                        mtu = 1500,
                        auto = true,
                        address = new { ipv4 = "10.0.0.1/24" },
                        route = new
                        {
                            ipv4 = new[] { "0.0.0.0/0" }
                        }
                    }
                };

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
                _logger.Log("=== GENERATED CONFIG ===");
                _logger.Log(json);
                _logger.Log("========================");
                File.WriteAllText(ConfigFile, json);
                return json;
            }
            catch (Exception ex)
            {
                throw new Exception($"Config Error: {ex.Message}");
            }
        }
    }
}