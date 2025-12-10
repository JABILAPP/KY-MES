using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using System.Net;

namespace KY_MES.Domain.V1.Interfaces
{
    public interface IKY_MESApplication
    {
        Task<long> SPISendWipData(SPIInputModel sPIInput);
        // Task<List<InspectionUnitRecord>> SPISendWipDataLog(SPIInputModel input);
        Task<long> SPISendWipDataLog(SPIInputModel input);
        //Task<List<InspectionUnitRecord>> BuildInspectionUnitRecords(SPIInputModel input, OperationInfo operationhistory);


        Task <long> VenusSPISendWipData(SPIInputModel sPIInput);

    }
}
