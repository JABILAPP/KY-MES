using KY_MES.Application.Exceptions;
using KY_MES.Application.Helpers;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.Interfaces;
using KY_MES.Services.DomainServices.Interfaces;
using System.Net.Http.Headers;
using CheckPVFailedException = KY_MES.Application.Exceptions.CheckPVFailedException;
//using CompleteWipException = KY_MES.Application.CompleteWipException;
using StartWipException = KY_MES.Application.Exceptions.StartWipException;
using UtilsModel = KY_MES.Application.App.Utils.UtilsModel;

namespace KY_MES.Controllers
{
    public class KY_MESApplication : IKY_MESApplication
    {
        private readonly IMESService _mes;
        private readonly ISpiRepository _repo;
        private readonly SPIHelpers _helpers;
        private readonly UtilsModel _utils;

        public KY_MESApplication(IMESService mes, ISpiRepository repo, SPIHelpers helpers)
        {
            _mes = mes;
            _repo = repo;
            _helpers = helpers;
            _utils = new UtilsModel();
        }

        public async Task<long> SmartPhoneSendWipData(SPIInputModel input)
        {
            var opHistory = await _mes.GetOperationInfoAsync(input.Inspection.Barcode);

            var customerName = opHistory.CustomerName;

            _helpers.KeepOneDefectPerCRDIgnoringEmptyComp(input);
            var remapped = await _helpers.MapearDefeitosSPICriandoNovo(input);

            var getWip = await _mes.GetWipIdBySerialNumberAsync(_utils.SpiToGetWip(remapped));
            if (getWip?.WipId == null || getWip.WipId <= 0) throw new Exception("WipId não encontrado");

            var wipPrincipal = (int)getWip.WipId;
            var wipIds = await _mes.GetWipIds(remapped.Inspection.Barcode!);

            // Validação SIZE/GB
            await _helpers.ValidateSizeGbIfNeeded(remapped, wipPrincipal);

            var isNg = remapped.Inspection.Result?.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0;
            var isSPIMachine = _helpers.IsSPIMachine(remapped.Inspection.Machine);

            CompleteWipResponseModel? completeWipResponse = null;


            if (isNg)
            {
                if (isSPIMachine)
                {
                    // NG para SPI
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");

                    var complete = await _helpers.TryAddDefectWithRetry(
                        () => _mes.AddDefectAsync(_utils.ToAddDefect(remapped, getWip), wipPrincipal),
                        maxRetries: 2,
                        delayMs: 500
                    );
                    if (complete == null) throw new CompleteWipException("complete wip failed");
                }
                else
                {
                    // NG PARA AOI
                    await _helpers.ValidateProgramVsBomOrBotForAOI(remapped, wipPrincipal, _mes);

                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");

                    var complete = await _helpers.TryAddDefectWithRetry(
                        () => _mes.AddDefectAsync(_utils.ToAddDefect(remapped, getWip), wipPrincipal),
                        maxRetries: 2,
                        delayMs: 500
                    );
                    if (complete == null) throw new CompleteWipException("complete wip failed");
                }
            }
            else
            {
                if (isSPIMachine)
                {
                    // OK para SS-DL / SP-DL (SPI)
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);
                    var resourceFromLog = remapped.Inspection.Machine;


                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");


                    completeWipResponse = await _mes.CompleteWipPassAsync(
                        _utils.ToCompleteWipPass(remapped, getWip), getWip.WipId.ToString()
                    );

                }
                else
                {
                    // OK para AOI 
                    await _helpers.ValidateProgramVsBomOrBotForAOI(remapped, wipPrincipal, _mes);

                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var resourceFromLog = remapped.Inspection.Machine;

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");


                    completeWipResponse = await _mes.CompleteWipPassAsync(
                        _utils.ToCompleteWipPass(remapped, getWip), getWip.WipId.ToString()
                    );

                }
            }

            // 6) db units + defects + runs
            try
            {
                var units = await _helpers.BuildInspectionUnitRecords(remapped, opHistory, _mes);
                var run = _helpers.BuildInspectionRun(remapped, opHistory?.ManufacturingArea, opHistory);
                var runId = await _repo.SaveSpiRunAsync(run, units);
                return runId;
            }
            catch
            {
                return 0;
            }
        }


        public async Task<long> SPISendWipDataLog(SPIInputModel input)
        {
            var username = Environment.GetEnvironmentVariable("Username");
            var password = Environment.GetEnvironmentVariable("Password");
            await _mes.SignInAsync(_utils.SignInRequest(username, password));

            var getWip = await _mes.GetWipIdBySerialNumberAsync(_utils.SpiToGetWip(input));
            if (getWip?.WipId == null || getWip.WipId <= 0) throw new Exception("WipId não encontrado");

            var opHistory = await _mes.GetOperationInfoAsync(input.Inspection.Barcode);

            var customerName = opHistory.CustomerName;

            var units = await _helpers.BuildInspectionUnitRecords(input, opHistory, _mes);

            var run = _helpers.BuildInspectionRun(input, opHistory?.ManufacturingArea, opHistory);
            var runId = await _repo.SaveSpiRunAsync(run, units);
            return runId;
        }


        public async Task <long> NotebookSendWipData(SPIInputModel input)
        {
            var opHistory = await _mes.GetOperationInfoAsync(input.Inspection.Barcode);

            _helpers.KeepOneDefectPerCRDIgnoringEmptyComp(input);
            var remapped = await _helpers.MapearDefeitosSPICriandoNovo(input);

            var getWip = await _mes.GetWipIdBySerialNumberAsync(_utils.SpiToGetWip(remapped));
            if (getWip?.WipId == null || getWip.WipId <= 0) throw new Exception("WipId não encontrado");

            var wipPrincipal = (int)getWip.WipId;
            var wipIds = await _mes.GetWipIds(remapped.Inspection.Barcode!);


            var isNg = remapped.Inspection.Result?.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0;
            var isSPIMachine = _helpers.IsSPIMachine(remapped.Inspection.Machine);

            CompleteWipResponseModel? completeWipResponse = null;

            if (isNg)
            {
                if (isSPIMachine)
                {
                    // NG para SPI
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");

                    var complete = await _helpers.TryAddDefectWithRetry(
                        () => _mes.AddDefectAsyncVenus(_utils.ToAddDefectVenus(remapped, getWip), wipPrincipal),
                        maxRetries: 2,
                        delayMs: 500
                    );

                    if (complete == null) throw new CompleteWipException("complete wip failed");
                }
                else
                {
                    // NG PARA AOI
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");

                    var complete = await _helpers.TryAddDefectWithRetry(
                        () => _mes.AddDefectAsyncVenus(_utils.ToAddDefectVenus(remapped, getWip), wipPrincipal),
                        maxRetries: 2,
                        delayMs: 500
                    );
                    if (complete == null) throw new CompleteWipException("complete wip failed");
                }
            }
            else
            {
                if (isSPIMachine)
                {
                    // OK para SS-DL / SP-DL (SPI)
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);
                    var resourceFromLog = remapped.Inspection.Machine;


                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");


                    completeWipResponse = await _mes.CompleteWipPassAsync(
                        _utils.ToCompleteWipPass(remapped, getWip), getWip.WipId.ToString()
                    );

                }
                else
                {
                    // OK para AOI 
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var resourceFromLog = remapped.Inspection.Machine;

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");


                    completeWipResponse = await _mes.CompleteWipPassAsync(
                        _utils.ToCompleteWipPass(remapped, getWip), getWip.WipId.ToString()
                    );

                }
            }

            // 6) db units + defects + runs
            try
            {
                var units = await _helpers.BuildInspectionUnitRecords(remapped, opHistory, _mes);
                var run = _helpers.BuildInspectionRun(remapped, opHistory?.ManufacturingArea, opHistory);
                var runId = await _repo.SaveSpiRunAsync(run, units);
                return runId;
            }
            catch
            {
                return 0;
            }

        }



        public async Task<long> TabletSendWipData(SPIInputModel input)
        {
            var opHistory = await _mes.GetOperationInfoAsync(input.Inspection.Barcode);

            _helpers.KeepOneDefectPerCRDIgnoringEmptyComp(input);
            var remapped = await _helpers.MapearDefeitosSPICriandoNovo(input);

            var getWip = await _mes.GetWipIdBySerialNumberAsync(_utils.SpiToGetWip(remapped));
            if (getWip?.WipId == null || getWip.WipId <= 0) throw new Exception("WipId não encontrado");

            var wipPrincipal = (int)getWip.WipId;
            var wipIds = await _mes.GetWipIds(remapped.Inspection.Barcode!);


            var isNg = remapped.Inspection.Result?.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0;
            var isSPIMachine = _helpers.IsSPIMachine(remapped.Inspection.Machine);

            CompleteWipResponseModel? completeWipResponse = null;


            if (isNg)
            {
                if (isSPIMachine)
                {
                    // NG para SPI
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");

                    var complete = await _helpers.TryAddDefectWithRetry(
                        () => _mes.AddDefectAsync(_utils.ToAddDefect(remapped, getWip), wipPrincipal),
                        maxRetries: 2,
                        delayMs: 500
                    );

                    if (complete == null) throw new CompleteWipException("complete wip failed");
                }
                else
                {
                    // NG PARA AOI
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");

                    var complete = await _helpers.TryAddDefectWithRetry(
                        () => _mes.AddDefectAsync(_utils.ToAddDefect(remapped, getWip), wipPrincipal),
                        maxRetries: 2,
                        delayMs: 500
                    );

                    if (complete == null) throw new CompleteWipException("complete wip failed");
                }
            }
            else
            {
                if (isSPIMachine)
                {
                    // OK para SS-DL / SP-DL (SPI)
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);
                    var resourceFromLog = remapped.Inspection.Machine;


                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");


                    completeWipResponse = await _mes.CompleteWipPassAsync(
                        _utils.ToCompleteWipPass(remapped, getWip), getWip.WipId.ToString()
                    );

                }
                else
                {
                    // OK para AOI 
                    var resourceMachine = _helpers.BuildResourceMachine(opHistory?.ManufacturingArea, "- Repair 01");
                    await _helpers.ExecutarReworkSeNecessario(_mes, wipIds, resourceMachine, wipPrincipal);

                    var resourceFromLog = remapped.Inspection.Machine;

                    var ok = await _mes.OkToStartAsync(_utils.ToOkToStart(remapped, getWip));
                    if (ok == null || !ok.OkToStart) throw new CheckPVFailedException("Check PV failed");

                    var start = await _mes.StartWipAsync(_utils.ToStartWip(remapped, getWip));
                    if (start == null || !start.Success) throw new StartWipException("start Wip failed");


                    completeWipResponse = await _mes.CompleteWipPassAsync(
                        _utils.ToCompleteWipPass(remapped, getWip), getWip.WipId.ToString()
                    );

                }
            }

            // 6) db units + defects + runs
            try
            {
                var units = await _helpers.BuildInspectionUnitRecords(remapped, opHistory, _mes);
                var run = _helpers.BuildInspectionRun(remapped, opHistory?.ManufacturingArea, opHistory);
                var runId = await _repo.SaveSpiRunAsync(run, units);
                return runId;
            }
            catch
            {
                return 0;
            }

        }




    }
}