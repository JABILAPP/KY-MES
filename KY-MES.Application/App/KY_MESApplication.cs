using KY_MES.Application.Utils;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Domain.V1.Interfaces;
using KY_MES.Services.DomainServices.Interfaces;
using System.Collections.Concurrent;
using System.Net;

namespace KY_MES.Controllers
{
    public class KY_MESApplication : IKY_MESApplication
    {
        private readonly IMESService _mESService;
        private readonly Utils utils;


        
        public KY_MESApplication(IMESService mESService)
        {
            _mESService = mESService;
            utils = new Utils();
        }

        public async Task<HttpStatusCode> SPISendWipData(SPIInputModel sPIInput)
        {
            var username = "svchua_jesmapistg";
            var password = "qKzla3oBDA51Ecq=+B2_z";

            await _mESService.SignInAsync(utils.SignInRequest(username, password));

            var getWipResponse = await _mESService.GetWipIdBySerialNumberAsync(utils.SpiToGetWip(sPIInput));

            if (getWipResponse.WipId == null)
            {
                return HttpStatusCode.BadRequest;
                throw new Exception("WipId is null");
            }


            // 1. Capturar o wip ID do produto e resorce name
            var serialNumber = sPIInput.Inspection.Barcode;
            var wipPrincipal = getWipResponse.WipId;
            var resourceMachine = "NEGRO - Repair 01";
            
            var wipids = await _mESService.GetWipIds(serialNumber!);


            //var completeWipResponse = sPIInput.Inspection.Result.Contains("NG")
            //    //? await _mESService.CompleteWipFailAsync(utils.ToCompleteWipFail(sPIInput, getWipResponse), getWipResponse.WipId.ToString())
            //    ? await utils.AddDefectToCompleteWip(_mESService.AddDefectAsync(utils.ToAddDefect(sPIInput, getWipResponse), getWipResponse.WipId.ToString()))
            //    : await _mESService.CompleteWipPassAsync(utils.ToCompleteWipPass(sPIInput, getWipResponse), getWipResponse.WipId.ToString());

            //At request of Louise and Elson
            //Added logic that retries adding defects up to 10 times if it fails, with a slight delay between attempts.



            CompleteWipResponseModel? completeWipResponse = null;

            Task.Delay(2000);

            if (sPIInput.Inspection.Result.Contains("NG"))
            {

                var resourceMachineGood = "NEGRO - Repair 01";
                
                var wipidsGood = await _mESService.GetWipIds(serialNumber!);

                // Ir o ListDefect e verificar se retornam vazios ou nao
                foreach (var wip in wipids)
                {
                    var indictmentIds = await _mESService.GetIndictmentIds(wip.WipId);

                    if (indictmentIds.Count > 0)
                    {
                        await _mESService.OkToStartRework(wip.WipId, resourceMachineGood!, wip.SerialNumber);

                        foreach (var indictmentId in indictmentIds)
                        {
                            await _mESService.AddRework(wip.WipId, indictmentId);
                        }

                        await _mESService.CompleteRework(wipPrincipal);

                        // return HttpStatusCode.OK;
                    }
                }



                var okToTestResponse = await _mESService.OkToStartAsync(utils.ToOkToStart(sPIInput, getWipResponse));
                if (!okToTestResponse.OkToStart || okToTestResponse == null)
                {
                    return HttpStatusCode.BadRequest;
                    throw new Exception("Check PV failed");
                }

                var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(sPIInput, getWipResponse));
                if (!startWipResponse.Success || startWipResponse == null)
                {
                    return HttpStatusCode.BadRequest;
                    throw new Exception("start Wip failed");
                }

                
                int retryCount = 0;
                int maxRetries = 10; 

                do
                {
                    try
                    {
                        completeWipResponse = await utils.AddDefectToCompleteWip(
                            _mESService.AddDefectAsync(
                                utils.ToAddDefect(sPIInput, getWipResponse),
                                getWipResponse.WipId
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= maxRetries)
                        {
                            throw new Exception($"Failed to add defect after {maxRetries} retries. Message: {ex.Message}");
                        }
                        await Task.Delay(500); // Add slight delay before retrying
                    }
                }
                while (completeWipResponse == null && retryCount < maxRetries);
            }
            else
            {


                var resourceMachineGood = "NEGRO - Repair 01";
                
                var wipidsGood = await _mESService.GetWipIds(serialNumber!);

                // Ir o ListDefect e verificar se retornam vazios ou nao
                foreach (var wip in wipids)
                {
                    var indictmentIds = await _mESService.GetIndictmentIds(wip.WipId);

                    if (indictmentIds.Count > 0)
                    {
                        await _mESService.OkToStartRework(wip.WipId, resourceMachineGood!, wip.SerialNumber);

                        foreach (var indictmentId in indictmentIds)
                        {
                            await _mESService.AddRework(wip.WipId, indictmentId);
                        }

                        await _mESService.CompleteRework(wipPrincipal);

                        // return HttpStatusCode.OK;
                    }
                }

                var okToTestResponse = await _mESService.OkToStartAsync(utils.ToOkToStart(sPIInput, getWipResponse));
                if (!okToTestResponse.OkToStart || okToTestResponse == null)
                {
                    return HttpStatusCode.BadRequest;
                    throw new Exception("Check PV failed");
                }

                var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(sPIInput, getWipResponse));
                if (!startWipResponse.Success || startWipResponse == null)
                {
                    return HttpStatusCode.BadRequest;
                    throw new Exception("start Wip failed");
                }


                completeWipResponse = await _mESService.CompleteWipPassAsync(
                    utils.ToCompleteWipPass(sPIInput, getWipResponse), getWipResponse.WipId.ToString()
                );
            }

            if (completeWipResponse.Equals(null))
            {
                return HttpStatusCode.BadRequest;
                throw new Exception("complete wip failed");
            }


           
            return HttpStatusCode.OK;
        }
    }
}