using Newtonsoft.Json;

namespace KY_MES.Domain.V1.DTOs.InputModels
{
    public class SpiLog : SPIInputModel
    {
        [JsonProperty("ManufacturingArea")]
        public string ManufacturingArea { get; set; }

        public SpiLog() { }

        public SpiLog(string manufacturingArea, SPIInputModel baseData)
        {
            ManufacturingArea = manufacturingArea;
            Inspection = baseData.Inspection;
            Board = baseData.Board;
        }
    }

    public class InspectionUnitRecord
    {
        public string? UnitBarcode { get; set; }
        public int ArrayIndex { get; set; }
        public string? Result { get; set; }
        public string? Side { get; set; }
        public string? Machine { get; set; }
        public string? User { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? ManufacturingArea { get; set; }
        public List<NormalizedDefect> Defects { get; set; } = new();
        public string? Pallet { get; set; }
    }

    public class NormalizedDefect
    {
        public string? Comp { get; set; }
        public string? Part { get; set; }
        public string DefectCode { get; set; } = "";
    }



    public class InspectionRun
    {
        public long Id { get; set; }
        public string? InspectionBarcode { get; set; }
        public string? Result { get; set; }
        public string? Program { get; set; }
        public string? Side { get; set; }
        public string? Stencil { get; set; }
        public string? Machine { get; set; }
        public string? User { get; set; }
        public DateTimeOffset? StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
        public string? ManufacturingArea { get; set; }
        public string? RawJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<InspectionUnit> Units { get; set; } = new();
        public string? Pallet { get; set; }
    }

    public class InspectionUnit
    {
        public long Id { get; set; }
        public long InspectionRunId { get; set; }
        public int ArrayIndex { get; set; }
        public string? UnitBarcode { get; set; }
        public string? Result { get; set; }
        public string? Side { get; set; }
        public string? Machine { get; set; }
        public string? User { get; set; }
        public DateTimeOffset? StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
        public string? ManufacturingArea { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public InspectionRun Run { get; set; } = null!;
        public List<InspectionDefect> Defects { get; set; } = new();
        public string? Pallet { get; set; }
    }

    public class InspectionDefect
    {
        public long Id { get; set; }
        public long InspectionUnitId { get; set; }
        public string? Comp { get; set; }
        public string? Part { get; set; }
        public string DefectCode { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public InspectionUnit Unit { get; set; } = null!;
        public string? Pallet { get; set; }
    }
    

    public class SPIWipInfo
    {
        public long WipId { get; set; }
        public string SerialNumber { get; set; }
        public PanelInfo Panel { get; set; }
        public string CustomerName { get; set; }
        public string MaterialName { get; set; }
        public bool IsAssembled { get; set; }
    }

    public class PanelInfo
    {
        public long PanelId { get; set; }
        public string PanelSerialNumber { get; set; }
        public string ConfiguredWipPerPanel { get; set; }
        public string ActualWipPerPanel { get; set; }
        public List<PanelWip> PanelWips { get; set; }
    }


}