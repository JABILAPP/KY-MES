using Newtonsoft.Json;

using System;

using System.Collections.Generic;

using System.Linq;

using System.Text;

using System.Threading.Tasks;

namespace KY_MES.Domain.V1.DTOs.OutputModels

{

    public class AddDefectRequestModel

    {

        public long? wipId { get; set; }

        public List<Defect> defects { get; set; }

        public bool? hasValidNumericField { get; set; }

        public List<PanelDefect> panelDefects { get; set; }
    }


    public class AddDefectRequestModelVenus

    {

        public long? wipId { get; set; }

        public List<Defect> defects { get; set; }

        public bool? hasValidNumericField { get; set; }


    }

    public class PanelDefect

    {

        public long? wipId { get; set; }

        public List<Defect> defects { get; set; }

        public bool? hasValidNumericField { get; set; }

    }

    public class Defect

    {

        public string? defectId { get; set; }

        public string? defectName { get; set; }

        public string? defectCRD { get; set; }

        public string? defectComment { get; set; }

    }

}

