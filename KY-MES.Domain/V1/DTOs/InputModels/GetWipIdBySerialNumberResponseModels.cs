using System;
using System.Collections.Generic;

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
namespace KY_MES.Domain.V1.DTOs.InputModels
{

    public partial class GetWipIdBySerialNumberResponseModels
    {
        [JsonProperty("wipId")]
        public int WipId { get; set; }

        [JsonProperty("serialNumber")]
        public string? SerialNumber { get; set; }

        [JsonProperty("panel")]
        public Panel? Panel { get; set; }

        [JsonProperty("customerName")]
        public string? CustomerName { get; set; }

        [JsonProperty("materialName")]
        public string? MaterialName { get; set; }

        [JsonIgnore]
        public bool HasMultipleBoards => Panel?.PanelWips != null && Panel.PanelWips.Any();

    }

    public partial class Panel
    {
        [JsonProperty("panelId")]
        public long? PanelId { get; set; }

        [JsonProperty("panelSerialNumber")]
        public string? PanelSerialNumber { get; set; }

        [JsonProperty("configuredWipPerPanel")]
        public string? ConfiguredWipPerPanel { get; set; }

        [JsonProperty("actualWipPerPanel")]
        public string? ActualWipPerPanel { get; set; }

        [JsonProperty("panelWips")]
        public List<PanelWip>? PanelWips { get; set; }
    }

    public partial class PanelWip
    {
        [JsonProperty("wipId")]
        public long? WipId { get; set; }

        [JsonProperty("serialNumber")]
        public string? SerialNumber { get; set; }

        [JsonProperty("panelPosition")]
        public long? PanelPosition { get; set; }

        [JsonProperty("isPanelBroken")]
        public bool? IsPanelBroken { get; set; }
    }
}
