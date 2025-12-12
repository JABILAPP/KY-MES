using System.Text.Json.Serialization;

namespace KY_MES.Domain.V1.DTOs.InputModels
{
    public class FullPerformWipOperationsRequest
    {
        [JsonPropertyName("siteName")]
        public string? SiteName { get; set; }
        [JsonPropertyName("customerName")]
        public string? CustomerName { get; set; }
        [JsonPropertyName("serialNumber")]
        public string? SerialNumber { get; set; }
        [JsonPropertyName("materialName")]
        public string? MaterialName { get; set; }
        [JsonPropertyName("resourceName")]
        public string? ResourceName { get; set; }
        [JsonPropertyName("startDateTime")]
        public string StartDateTime { get; set; } = string.Empty;
        [JsonPropertyName("endDateTime")]
        public string EndDateTime { get; set; } = string.Empty;
        [JsonPropertyName("isSingleWipMode")]
        public bool IsSingleWipMode { get; set; } = false;

    }
}
