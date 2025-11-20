using System.Text.Json.Serialization;

public class CompleteWithPanelDefectsRequest
{
    [JsonPropertyName("wipId")]
    public long WipId { get; set; }

    [JsonPropertyName("panelDefectList")]
    public List<PanelDefectItem>? PanelDefectList { get; set; }
}

public class PanelDefectItem
{
    [JsonPropertyName("wipId")]
    public long WipId { get; set; }

    [JsonPropertyName("defects")]
    public List<DefectItem>? Defects { get; set; }
}

public class DefectItem
{
    [JsonPropertyName("defectName")]
    public string? DefectName { get; set; }

    [JsonPropertyName("defectCRD")]
    public string? DefectCRD { get; set; }

    [JsonPropertyName("defectDetail")]
    public string? DefectDetail { get; set; }

    [JsonPropertyName("defectComment")]
    public string? DefectComment { get; set; }

    [JsonPropertyName("defectQuantity")]
    public int DefectQuantity { get; set; } = 1;
}