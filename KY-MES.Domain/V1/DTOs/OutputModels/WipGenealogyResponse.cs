using KY_MES.Application.App.Utils;
using System.Text.Json.Serialization;

namespace KY_MES.Domain.V1.DTOs.OutputModels
{
    public class WipGenealogyItem
    {
        [JsonPropertyName("Level")]
        public int Level { get; set; }

        [JsonPropertyName("ItemId")]
        public int ItemId { get; set; }

        [JsonPropertyName("ParentItemId")]
        public int ParentItemId { get; set; }

        [JsonPropertyName("MaterialId")]
        public int MaterialId { get; set; }

        [JsonPropertyName("MaterialName")]
        public string? MaterialName { get; set; }

        [JsonPropertyName("PinCount")]
        public int PinCount { get; set; }

        [JsonPropertyName("PhoenixMaterialType")]
        public string? PhoenixMaterialType { get; set; }

        [JsonPropertyName("SerializedMaterialId")]
        public int? SerializedMaterialId { get; set; }

        [JsonPropertyName("WipId")]
        public int? WipId { get; set; }

        [JsonPropertyName("SerialNumber")]
        public string? SerialNumber { get; set; }

        [JsonPropertyName("ManufacturerPartNumber")]
        public string? ManufacturerPartNumber { get; set; }

        [JsonPropertyName("Quantity")]
        [JsonConverter(typeof(NullableInt32Converter))]
        public int? Quantity { get; set; }

        [JsonPropertyName("CRD")]
        public string? CRD { get; set; }

        [JsonPropertyName("AssembledDateTime")]
        public DateTimeOffset? AssembledDateTime { get; set; }

        [JsonPropertyName("AssembledLocation")]
        public string? AssembledLocation { get; set; }

        [JsonPropertyName("ReturnCount")]
        public int ReturnCount { get; set; }

        [JsonPropertyName("DataCollections")]
        public object? DataCollections { get; set; }

        [JsonPropertyName("UPDs")]
        public object? UPDs { get; set; }

        [JsonPropertyName("InventoryID")]
        public string? InventoryID { get; set; }
    }

    public class WipGenealogyResponse
    {
        [JsonPropertyName("WipGenealogy")]
        public List<WipGenealogyItem>? WipGenealogy { get; set; }
    }
}
