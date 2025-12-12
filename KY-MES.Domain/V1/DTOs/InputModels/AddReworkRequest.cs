using Newtonsoft.Json;

namespace KY_MES.Domain.V1.DTOs.InputModels
{
    public sealed class AddReworkRequest
    {
        [JsonProperty("wipId")]
        public int WipId { get; set; }

        [JsonProperty("reworkCategory")]
        public string ReworkCategory { get; set; } = "Rework";

        [JsonProperty("detail")]
        public string Detail { get; set; } = "";

        [JsonProperty("comment")]
        public string Comment { get; set; } = "";

        [JsonProperty("indictmentId")]
        public int IndictmentId { get; set; }

        [JsonProperty("replaceDetail")]
        public ReplaceDetail ReplaceDetail { get; set; } = new ReplaceDetail();
    }

    public sealed class ReplaceDetail
    {
        [JsonProperty("serialNumber")]
        public string SerialNumber { get; set; }

        [JsonProperty("grn")]
        public string Grn { get; set; }

        [JsonProperty("upds")]
        public List<UpdItem> Upds { get; set; } = new List<UpdItem>();

        [JsonProperty("dataCollectionItems")]
        public List<DataCollectionItem> DataCollectionItems { get; set; } = new List<DataCollectionItem>();
    }

    public sealed class UpdItem
    {
        [JsonProperty("updName")]
        public string UpdName { get; set; }

        [JsonProperty("updValue")]
        public string UpdValue { get; set; }
    }

    public sealed class DataCollectionItem
    {
        [JsonProperty("dataLabel")]
        public string DataLabel { get; set; }

        [JsonProperty("measureValue")]
        public string MeasureValue { get; set; }
    }
}