using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KY_MES.Domain.V1.DTOs.InputModels
{
    public class MeasurementModel
    {

        [JsonProperty("measurementLabel")]
        public string MeasurementLabel { get; set; }

        [JsonProperty("measurementData")]
        public string MeasurementData { get; set; }
        [JsonProperty("measureStatus")]
        public string MeasureStatus { get; set; }

    }
}
