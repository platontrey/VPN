using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HysteryVPN.Services
{
    public class GeoLocationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<(double lat, double lon)?> GetLocationByIPAsync(string ip = null)
        {
            try
            {
                // Если IP не указан, используем текущий IP
                string url = ip == null
                    ? "http://ip-api.com/json/"
                    : $"http://ip-api.com/json/{ip}";

                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response).RootElement;

                if (json.GetProperty("status").GetString() == "success")
                {
                    double lat = json.GetProperty("lat").GetDouble();
                    double lon = json.GetProperty("lon").GetDouble();
                    return (lat, lon);
                }
                else
                {
                    Console.WriteLine($"GeoLocation error: {json.GetProperty("message").GetString()}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting location: {ex.Message}");
                return null;
            }
        }
    }
}
