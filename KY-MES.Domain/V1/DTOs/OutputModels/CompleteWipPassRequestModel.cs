using KY_MES.Domain.V1.DTOs.InputModels;
using Newtonsoft.Json;

namespace KY_MES.Domain.V1.DTOs.OutputModels
{
    public class CompleteWipPassRequestModel
    {

        [JsonProperty("wipId")]
        public int WipId { get; set; }


        [JsonProperty("measurements")]
        public List<MeasurementModel> Measurements { get; set; } = new();
    }

}

