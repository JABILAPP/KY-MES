namespace KY_MES.Domain.V1.DTOs.OutputModels
{
    public sealed class WipBySerialResponseItem
    {
        public int? WipId { get; set; }
        public string SerialNumber { get; set; }
        public PanelInfo Panel { get; set; }
        public string CustomerName { get; set; }
        public string MaterialName { get; set; }
    }

    public sealed class PanelInfo
    {
        public int? PanelId { get; set; }
        public string PanelSerialNumber { get; set; }
        public string ConfiguredWipPerPanel { get; set; }
        public string ActualWipPerPanel { get; set; }
        public List<PanelWipItem> PanelWips { get; set; }
    }

    public sealed class PanelWipItem
    {
        public int? WipId { get; set; }
        public string SerialNumber { get; set; }
        public int? PanelPosition { get; set; }
        public bool? IsPanelBroken { get; set; }
    }
}