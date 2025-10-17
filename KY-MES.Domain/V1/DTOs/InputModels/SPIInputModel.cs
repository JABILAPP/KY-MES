using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace KY_MES.Domain.V1.DTOs.InputModels
{
    public class SPIInputModel
    {
        [JsonProperty("Inspection")]
        public Inspection? Inspection { get; set; }

        [JsonProperty("Board")]
        public List<Board>? Board { get; set; }

        [JsonProperty("Pallet")]
        public string? Pallet { get; set; }
    }

    public partial class Board
    {
        [JsonProperty("Array")]
        public int? Array { get; set; }

        [JsonProperty("Barcode")]
        public string? Barcode { get; set; }

        [JsonProperty("Result")]
        public string? Result { get; set; }

        [JsonProperty("Defects")]
        public List<Defects>? Defects { get; set; }
    }

    public partial class Defects
    {
        [JsonProperty("Comp")]
        public string? Comp { get; set; }

        [JsonProperty("Part")]
        public string? Part { get; set; }

        [JsonProperty("Defect")]
        public string? Defect{ get; set; }

        [JsonProperty("Review")]
        public string? Review { get; set; }
    }

    public partial class Inspection
    {
        [JsonProperty("Barcode")]
        public string? Barcode { get; set; }

        [JsonProperty("Result")]
        public string? Result { get; set; }

        [JsonProperty("Program")]
        public string? Program { get; set; }

        [JsonProperty("Side")]
        public string? Side { get; set; }

        [JsonProperty("Stencil")]
        public int? Stencil { get; set; }

        [JsonProperty("Machine")]
        public string? Machine { get; set; }

        [JsonProperty("User")]
        public string? User { get; set; }

        [JsonProperty("Start")]
        public string? Start { get; set; }

        [JsonProperty("End")]
        public string? End { get; set; }
    }
}
