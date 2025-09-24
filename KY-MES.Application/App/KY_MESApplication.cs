using KY_MES.Application.Utils;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Domain.V1.Interfaces;
using KY_MES.Services.DomainServices.Interfaces;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

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

        public async Task<SPIInputModel> SPISendWipData(SPIInputModel sPIInput)
        {
            var username = Environment.GetEnvironmentVariable("Username");
            var password = Environment.GetEnvironmentVariable("Password");

            await _mESService.SignInAsync(utils.SignInRequest(username, password));

            var operationhistory = await _mESService.GetOperationInfoAsync(sPIInput.Inspection.Barcode);

            // Deduplicação no objeto original (regra atual)
            SpiDefectUtils.KeepOneDefectPerCRDIgnoringEmptyComp(sPIInput);

            // Cria um clone remapeado (NÃO altera sPIInput)
            var sPIInputRemapped = await MapearDefeitosSPICriandoNovo(sPIInput);

            var getWipResponse = await _mESService.GetWipIdBySerialNumberAsync(utils.SpiToGetWip(sPIInput));
            if (getWipResponse.WipId == null)
                throw new Exception("WipId is null");

            // 1. Capturar o wip ID do produto e serial
            var serialNumber = sPIInput.Inspection.Barcode;
            var wipPrincipal = getWipResponse.WipId;
            var wipIdInts = await _mESService.GetWipIds(serialNumber!);

            CompleteWipResponseModel? completeWipResponse = null;

            await Task.Delay(2000);

            if (sPIInputRemapped.Inspection.Result.Contains("NG"))
            {
                // DIFERENCIAÇÃO DE INPUT PARA OS LOGS DE SPI 
                if (sPIInputRemapped.Inspection.Machine.StartsWith("SS-DL"))
                {
                    // RESOURCE MACHINE PARA SPI 
                    string? manufacturingArea = operationhistory.ManufacturingArea;
                    string suffix = "- Repair 01";

                    string resourceMachineSPI = string.IsNullOrWhiteSpace(manufacturingArea)
                        ? suffix
                        : $"{manufacturingArea.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()} {suffix}";

                    // Ir o ListDefect e verificar se retornam vazios ou nao
                    foreach (var wip in wipIdInts)
                    {
                        var indictmentIds = await _mESService.GetIndictmentIds(wip.WipId);

                        if (indictmentIds.Count > 0)
                        {
                            await _mESService.OkToStartRework(wip.WipId, resourceMachineSPI!, wip.SerialNumber);

                            foreach (var indictmentId in indictmentIds)
                            {
                                await _mESService.AddRework(wip.WipId, indictmentId);
                            }

                            await _mESService.CompleteRework(wipPrincipal);
                        }
                    }

                    var okToTestResponse = await _mESService.OkToStartAsync(utils.ToOkToStart(sPIInputRemapped, getWipResponse));
                    if (okToTestResponse == null || !okToTestResponse.OkToStart)
                        throw new Exception("Check PV failed");

                    var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(sPIInputRemapped, getWipResponse));
                    if (startWipResponse == null || !startWipResponse.Success)
                        throw new Exception("start Wip failed");

                    int retryCount = 0;
                    int maxRetries = 10;

                    do
                    {
                        try
                        {
                            completeWipResponse = await utils.AddDefectToCompleteWip(
                                _mESService.AddDefectAsync(
                                    utils.ToAddDefect(sPIInputRemapped, getWipResponse),
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
                            await Task.Delay(500);
                        }
                    }
                    while (completeWipResponse == null && retryCount < maxRetries);
                }
            }
            else
            {
                if (sPIInputRemapped.Inspection.Machine.StartsWith("SS-DL"))
                {
                    string? manufacturingArea = operationhistory.ManufacturingArea;
                    string suffix = "- Repair 01";

                    string resourceMachineSPI = string.IsNullOrWhiteSpace(manufacturingArea)
                        ? suffix
                        : $"{manufacturingArea.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()} {suffix}";

                    // Ir o ListDefect e verificar se retornam vazios ou nao
                    foreach (var wip in wipIdInts)
                    {
                        var indictmentIds = await _mESService.GetIndictmentIds(wip.WipId);

                        if (indictmentIds.Count > 0)
                        {
                            await _mESService.OkToStartRework(wip.WipId, resourceMachineSPI!, wip.SerialNumber);

                            foreach (var indictmentId in indictmentIds)
                            {
                                await _mESService.AddRework(wip.WipId, indictmentId);
                            }

                            await _mESService.CompleteRework(wipPrincipal);

                            // return HttpStatusCode.OK;
                        }
                    }

                    var okToTestResponse = await _mESService.OkToStartAsync(utils.ToOkToStart(sPIInputRemapped, getWipResponse));
                    if (okToTestResponse == null || !okToTestResponse.OkToStart)
                        throw new Exception("Check PV failed");

                    var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(sPIInputRemapped, getWipResponse));
                    if (startWipResponse == null || !startWipResponse.Success)
                        throw new Exception("start Wip failed");

                    completeWipResponse = await _mESService.CompleteWipPassAsync(
                        utils.ToCompleteWipPass(sPIInputRemapped, getWipResponse), getWipResponse.WipId.ToString()
                    );
                }
                else
                {
                    string? manufacturingArea = operationhistory.ManufacturingArea;
                    string suffix = "- Repair 01";

                    string resourceMachineAOI = string.IsNullOrWhiteSpace(manufacturingArea)
                        ? suffix
                        : $"{manufacturingArea.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()} {suffix}";

                    // Ir o ListDefect e verificar se retornam vazios ou nao
                    foreach (var wip in wipIdInts)
                    {
                        var indictmentIds = await _mESService.GetIndictmentIds(wip.WipId);

                        if (indictmentIds.Count > 0)
                        {
                            await _mESService.OkToStartRework(wip.WipId, resourceMachineAOI!, wip.SerialNumber);

                            foreach (var indictmentId in indictmentIds)
                            {
                                await _mESService.AddRework(wip.WipId, indictmentId);
                            }

                            await _mESService.CompleteRework(wipPrincipal);
                        }
                    }

                    var okToTestResponse = await _mESService.OkToStartAsync(utils.ToOkToStart(sPIInputRemapped, getWipResponse));
                    if (okToTestResponse == null || !okToTestResponse.OkToStart)
                        throw new Exception("Check PV failed");

                    var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(sPIInputRemapped, getWipResponse));
                    if (startWipResponse == null || !startWipResponse.Success)
                        throw new Exception("start Wip failed");

                    completeWipResponse = await _mESService.CompleteWipPassAsync(
                        utils.ToCompleteWipPass(sPIInputRemapped, getWipResponse), getWipResponse.WipId.ToString()
                    );
                }
            }

            if (completeWipResponse == null)
                throw new Exception("complete wip failed");

            return sPIInputRemapped;
        }

        public async Task<SPIInputModel> SPISendWipDataLog(SPIInputModel sPIInput)
        {
            var username = Environment.GetEnvironmentVariable("Username");
            var password = Environment.GetEnvironmentVariable("Password");

            await _mESService.SignInAsync(utils.SignInRequest(username, password));

            // Retornar clone remapeado
            var sPIInputRemapped = await MapearDefeitosSPICriandoNovo(sPIInput);
            return sPIInputRemapped;
        }

        // Método antigo que altera in-place (mantido caso ainda queira usar em algum lugar)
        void MapearDefeitosSPI(SPIInputModel spi)
        {
            var defectMap = ObterDefectMap();

            if (spi?.Board == null) return;

            foreach (var board in spi.Board)
            {
                if (board?.Defects == null) continue;

                foreach (var defect in board.Defects)
                {
                    var originalName = defect.Defect;
                    var key = originalName?.Trim();
                    if (key != null && defectMap.TryGetValue(key, out var mapped))
                    {
                        defect.Defect = mapped;
                        defect.Review = mapped;
                    }
                }
            }
        }

        private static readonly SemaphoreSlim NormalizeLock = new SemaphoreSlim(1, 1);

        // Nova versão: cria e retorna um novo objeto com defeitos mapeados
        private async Task <SPIInputModel> MapearDefeitosSPICriandoNovo(SPIInputModel spi, CancellationToken ct = default)
        {
            await NormalizeLock.WaitAsync(ct);
            try
            {
                var clone = DeepClone(spi);
                if (clone?.Board == null) return clone;

                var defectMap = ObterDefectMap();

                foreach (var board in clone.Board)
                {
                    if (board?.Defects == null) continue;

                    foreach (var defect in board.Defects)
                    {
                        var key = defect?.Defect?.Trim();
                        if (key != null && defectMap.TryGetValue(key, out var mapped))
                        {
                            defect.Defect = mapped;
                            defect.Review = mapped;
                        }
                    }
                }

                return clone;
            }
            finally
            {
                NormalizeLock.Release();
            }
        }

        private static Dictionary<string, string> ObterDefectMap()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UNUSED"] = "UNUSED",
                ["GOOD"] = "GOOD",
                ["PASS"] = "GOOD",
                ["BADMARK"] = "BADMARK",

                ["WARNING_EXCESSIVE_VOLUME"] = "Excess solder",
                ["WARNING_INSUFFICIENT_VOLUME"] = "Insuff solder",
                ["WARNING_POSITION"] = "Solder Paste Offset",
                ["WARNING_BRIDGING"] = "Short/Bridging",
                ["WARNING_GOLDTAB"] = "GOLD SURFACE CONTACT AREA PROBLEM",
                ["WARNING_SHAPE"] = "Incorrect Shape",
                ["WARNING_UPPER_HEIGHT"] = "Solder Paste Upper Height",
                ["WARNING_LOW_HEIGHT"] = "Solder Paste Low Height",
                ["WARNING_HIGH_AREA"] = "High Area",
                ["WARNING_LOW_AREA"] = "Low Area",
                ["WARNING_COPLANARITY"] = "Coplanarity",
                ["WARNING_SMEAR"] = "Disturbed solder",
                ["WARNING_FM"] = "SOLDER COVERAGE",
                ["WARNING_SURFACE"] = "SOLDER COVERAGE",

                ["NORMALIZE_HEIGHT"] = "SOLDER COVERAGE",
                ["ROI_NUMBER"] = "SOLDER COVERAGE",

                ["EXCESSIVE_VOLUME"] = "Excess solder",
                ["INSUFFICIENT_VOLUME"] = "Insuff solder",
                ["POSITION"] = "Solder Paste Offset",
                ["BRIDGING"] = "Short/Bridging",
                ["GOLDTAB"] = "GOLD SURFACE CONTACT AREA PROBLEM",
                ["SHAPE"] = "Incorrect Shape",
                ["UPPER_HEIGHT"] = "Solder Paste Upper Height",
                ["LOW_HEIGHT"] = "Solder Paste Low Height",
                ["HIGH_AREA"] = "High Area",
                ["LOW_AREA"] = "Low Area",
                ["COPLANARITY"] = "Coplanarity",
                ["SMEAR"] = "Disturbed solder",
                ["FM"] = "SOLDER COVERAGE",
                ["SURFACE"] = "SOLDER COVERAGE",

                ["REPAIRED"] = "REPAIRED",
                ["NG"] = "NG",
                ["UNDEFINED"] = "UNDEFINED",
                ["PADOVERHANG"] = "Misonserted/Misaligned",
                ["DIMENSION"] = "Wrong Part",
                ["MISSING"] = "Missing",
                ["COMPONENT_SHIFT"] = "Skewed",
                ["UPSIDEDOWN"] = "Upside down",
                ["SOLDER_JOINT"] = "Insuff solder",
                ["LIFTED_LEAD"] = "Lifted lead",
                ["LIFTED_BODY"] = "Coplanarity",
                ["BILL_BOARDING"] = "Billboarding",
                ["TOMBSTONE"] = "Tombstone",
                ["BODY_DIMENSION"] = "Wrong Part",
                ["POLARITY"] = "Wrong polarit/reversed",
                ["OCR_OCV"] = "OCV Fail",
                ["ABSENCE"] = "Extra part",
                ["OVERHANG"] = "Misonserted/Misaligned",
                ["MISSING_LEAD"] = "Missing Lead",
                ["PARTICLE"] = "PARTICLE",
                ["FOREIGNMATERIAL_BODY"] = "Foreign material / Particulate matter",
                ["FOREIGNMATERIAL_LEAD"] = "Foreign material / Particulate matter",
            };
        }

        private static T DeepClone<T>(T obj)
        {
            if (obj == null) return default!;
            var json = JsonSerializer.Serialize(
                obj,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    // Se houver referências cíclicas entre objetos do modelo, descomente:
                    // ReferenceHandler = ReferenceHandler.Preserve
                }
            );
            return JsonSerializer.Deserialize<T>(json)!;
        }
    }

    public static class SpiDefectUtils
    {
        public static void KeepOneDefectPerBoard(SPIInputModel input)
        {
            if (input?.Board == null) return;

            foreach (var b in input.Board)
            {
                if (b?.Defects == null || b.Defects.Count == 0) continue;

                var first = b.Defects[0];
                b.Defects = new List<KY_MES.Domain.V1.DTOs.InputModels.Defects> { first };
            }
        }

        public static void DeduplicateDefectsByComp(SPIInputModel input)
        {
            if (input?.Board == null) return;

            foreach (var panel in input.Board)
            {
                if (panel?.Defects == null || panel.Defects.Count == 0) continue;

                panel.Defects = panel.Defects
                    .GroupBy(d => (d.Comp ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }
        }

        public static void KeepOneDefectPerCRDPerBoard(SPIInputModel input)
        {
            if (input?.Board == null) return;

            foreach (var b in input.Board)
            {
                if (b?.Defects == null || b.Defects.Count == 0) continue;

                b.Defects = b.Defects
                    .GroupBy(d => (d.Comp ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }
        }

        public static void KeepOneDefectPerCRDIgnoringEmptyComp(SPIInputModel input)
        {
            if (input?.Board == null) return;

            foreach (var b in input.Board)
            {
                if (b?.Defects == null || b.Defects.Count == 0) continue;

                b.Defects = b.Defects
                    .Where(d => !string.IsNullOrWhiteSpace(d.Comp))
                    .GroupBy(d => d.Comp.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }
        }
    }
}