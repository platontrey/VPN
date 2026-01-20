using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HysteryVPN.Models
{
    public class GeoJsonFeatureCollection
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("features")]
        public List<GeoJsonFeature> Features { get; set; }
    }

    public class GeoJsonFeature
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("properties")]
        public GeoJsonProperties Properties { get; set; }

        [JsonPropertyName("geometry")]
        public GeoJsonGeometry Geometry { get; set; }
    }

    public class GeoJsonProperties
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("ISO3166-1-Alpha-3")]
        public string Iso3 { get; set; }

        [JsonPropertyName("ISO3166-1-Alpha-2")]
        public string Iso2 { get; set; }
    }

    public class GeoJsonGeometry
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("coordinates")]
        public JsonElement Coordinates { get; set; }
    }
}
