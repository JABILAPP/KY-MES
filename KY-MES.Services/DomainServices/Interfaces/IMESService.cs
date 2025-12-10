using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KY_MES.Services.DomainServices.Interfaces
{
    public interface IMESService
    {
        Task SignInAsync(SignInRequestModel signInRequestModel);
        Task<GetWipIdBySerialNumberResponseModels> GetWipIdBySerialNumberAsync(GetWipIdBySerialNumberRequestModel getWipIdRequestModel);
        Task<OkToStartResponseModel> OkToStartAsync(OkToStartRequestModel okToStartRequestModel);
        Task<StartWipResponseModel> StartWipAsync(StartWipRequestModel startWipRequestModel);
        Task<CompleteWipResponseModel> CompleteWipFailAsync(CompleteWipFailRequestModel completWipRequestModel, string WipId);
        Task<AddDefectResponseModel> AddDefectAsync(AddDefectRequestModel addDefectRequestModel, int WipId);
        Task<AddDefectResponseModel> AddDefectAsyncVenus(AddDefectRequestModelVenus addDefectRequestModel, int WipId);
        Task<CompleteWipResponseModel> CompleteWipPassAsync(CompleteWipPassRequestModel completWipRequestModel, string WipId);



        // new methods
        Task<List<int>> GetIndictmentIds(int wipId);
        Task<List<WipSerial>> GetWipIds(string serialNumber);
        Task OkToStartRework(int wipId, string resourceName, string serialNumber);
        Task AddRework(int wipId, int indicmentId);
        Task CompleteRework(int wipId);
        Task<OperationInfo?> GetOperationInfoAsync(string serialNumber);
        Task<List<SPIWipInfo>> GetPanelWipInfoAsync(string runBarCode, CancellationToken ct = default);



        // pallet tracking 
        Task AddAttribute(SPIInputModel input);



        // BOM comparing 
        Task<string> GetProgramInBom(int assemblyId);
        Task<string> GetProgramInBomSPI(int assemblyId);
        Task<int> GetAssemblyId(int wipId);



        // full operation complete pass 
        Task<CompleteWipResponseModel> FullWipOpCompletePass(OperationInfo operationInfo, GetWipIdBySerialNumberResponseModels getWipResponse);
    }
}
