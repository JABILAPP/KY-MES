using System.Text.Json.Serialization;

namespace KY_MES.Domain.V1.DTOs.AddAttributeModel
{
    public class AddAttributeDto
    {
        [JsonPropertyName("attributeName")]
        public string? AttributeName { get; set; }

        [JsonPropertyName("attributeType")]
        public string? AttributeType { get; set; }

        [JsonPropertyName("attributeValue")]
        public string? AttributeValue { get; set; }

        [JsonPropertyName("panelAttributeList")]
        public List<PanelAttributeList>? PanelAttributeList { get; set; }

        [JsonPropertyName("wipId")]
        public int? WipId { get; set; }
    }

    public class PanelAttributeList
    {
        [JsonPropertyName("wipId")]
        public int? WipId { get; set; }

        [JsonPropertyName("attributeAssignments")]
        public List<AttributeAssignments>? AttributeAssignments { get; set; }
    }

    public class AttributeAssignments
    {
        [JsonPropertyName("attributeName")]
        public string? AttributeName { get; set; }

        [JsonPropertyName("attributeType")]
        public string? AttributeType { get; set; }

        [JsonPropertyName("attributeValue")]
        public string? AttributeValue { get; set; }
    }
}
