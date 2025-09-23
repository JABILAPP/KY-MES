using KY_MES.Domain.V1.DTOs.InputModels;
using System.Net;

namespace KY_MES.Domain.V1.Interfaces
{
    public interface IKY_MESApplication
    {
        Task<HttpStatusCode> SPISendWipData(SPIInputModel sPIInput);
        Task<SPIInputModel> SPISendWipDataLog(SPIInputModel sPIInput);
    }
}
