using KY_MES.Application.Utils;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Domain.V1.Interfaces;
using KY_MES.Services.DomainServices.Interfaces;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using KY_MES.Domain.DefectMap;
using KY_MES.Domain.ModelType;

namespace KY_MES.Controllers
{
    public class KY_MESApplication : IKY_MESApplication
    {
        private readonly IMESService _mESService;
        private readonly Utils utils;
        private readonly ISpiRepository _repo;
        private readonly IConfiguration _configuration;

        public KY_MESApplication(IMESService mESService, ISpiRepository repo, IConfiguration configuration)
        {
            _mESService = mESService;
            utils = new Utils();
            _repo = repo;
            _configuration = configuration;
        }

        public async Task<long> SPISendWipData(SPIInputModel sPIInput)
        {
            // auth method
            var username = Environment.GetEnvironmentVariable("Username");
            var password = Environment.GetEnvironmentVariable("Password");
            await _mESService.SignInAsync(utils.SignInRequest(username, password));

            // get history for production line
            var operationhistory = await _mESService.GetOperationInfoAsync(sPIInput.Inspection.Barcode);

            // ignoring empty crds and keep only one defect per crd
            SpiDefectUtils.KeepOneDefectPerCRDIgnoringEmptyComp(sPIInput);

            // new dto 
            var sPIInputRemapped = await MapearDefeitosSPICriandoNovo(sPIInput);

            // get wip id
            var getWipResponse = await _mESService.GetWipIdBySerialNumberAsync(utils.SpiToGetWip(sPIInput));
            if (getWipResponse.WipId == null)
                throw new Exception("WipId is null");

            // 1. Capturar o wip ID do produto e serial
            var serialNumber = sPIInput.Inspection.Barcode;
            var wipPrincipal = getWipResponse.WipId;
            var wipIdInts = await _mESService.GetWipIds(serialNumber!);

            CompleteWipResponseModel? completeWipResponse = null;


            // Comparação de BOM TOP para o Program da AOI 
            var assemblyId = await _mESService.GetAssemblyId(wipPrincipal);
            var parentBom = await _mESService.GetProgramInBom(assemblyId);
            var parentBomSPI = await _mESService.GetProgramInBomSPI(assemblyId);


            // CHECKAGEM DE ASSEMBLY PELO DICIONARIO PASSANDO O PROGRAM DO LOG, COLOCAR UM CONTAINS 
            var programFromSPI = sPIInputRemapped.Inspection.Program;

            if (programFromSPI.Contains("GB"))
            {
                var assemblyModelMemory = await ObterTypeModelMemoryAsync();

                if (!assemblyModelMemory.TryGetValue(parentBomSPI, out var sizeFromDB))
                {
                    throw new Exception($"FERT {parentBomSPI} não encontrado no dicionário");
                }


                var sizeMatch = System.Text.RegularExpressions.Regex.Match(programFromSPI, @"-(\d{3})(?:GB)?-");
                if (sizeMatch.Success)
                {
                    var sizeFromSPI = sizeMatch.Groups[1].Value + "G"; // "128G" ou "256G"

                    if (sizeFromDB != sizeFromSPI)
                    {
                        throw new Exception($"Size não corresponde. Esperado: {sizeFromDB}, Recebido: {sizeFromSPI}");
                    }
                }
            }


            //await Task.Delay(2000);

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
                else
                {
                    var programFromAOI = sPIInputRemapped.Inspection.Program;
                    if (programFromAOI == parentBom)
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
                    else
                    {
                        throw new Exception("Program is different for this product");
                    }


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

                    var programFromAOI = sPIInputRemapped.Inspection.Program;

                    if (programFromAOI == parentBom)
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
                            throw new CheckPVFailedException("Check PV failed");

                        var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(sPIInputRemapped, getWipResponse));
                        if (startWipResponse == null || !startWipResponse.Success)
                            throw new Exception("start Wip failed");

                        completeWipResponse = await _mESService.CompleteWipPassAsync(
                            utils.ToCompleteWipPass(sPIInputRemapped, getWipResponse), getWipResponse.WipId.ToString()
                        );

                    }
                    else
                    {
                        throw new BomProgramFailException("Program is different for this product");
                    }

                        
                }
            }

            if (completeWipResponse == null)
                throw new Exception("complete wip failed");



            try
            {
                var units = await BuildInspectionUnitRecords(sPIInputRemapped);
                var opInfo = await _mESService.GetOperationInfoAsync(sPIInputRemapped.Inspection.Barcode!);
                var manufacturingArea = opInfo?.ManufacturingArea;

                var run = new InspectionRun
                {
                    InspectionBarcode = sPIInputRemapped.Inspection?.Barcode,
                    Result = sPIInputRemapped.Inspection?.Result,
                    Program = sPIInputRemapped.Inspection?.Program,
                    Side = sPIInputRemapped.Inspection?.Side,
                    Stencil = sPIInputRemapped.Inspection?.Stencil.ToString(),
                    Machine = sPIInputRemapped.Inspection?.Machine,
                    User = sPIInputRemapped.Inspection?.User,
                    StartTime = ParseDateOffset(sPIInputRemapped.Inspection?.Start),
                    EndTime = ParseDateOffset(sPIInputRemapped.Inspection?.End),
                    ManufacturingArea = manufacturingArea,
                    // RawJson = rawJson 
                };

                var runId = await _repo.SaveSpiRunAsync(run, units);
                return runId;
            
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

       

            
        public async Task<long> SPISendWipDataLog(SPIInputModel input)
        {

            var username = Environment.GetEnvironmentVariable("Username");
            var password = Environment.GetEnvironmentVariable("Password");


            await _mESService.SignInAsync(utils.SignInRequest(username, password));

            var getWipResponse = await _mESService.GetWipIdBySerialNumberAsync(utils.SpiToGetWip(input));

            var WipId = getWipResponse.WipId;

            var assemblyId = await _mESService.GetAssemblyId(WipId);

            var parentBom = await _mESService.GetProgramInBom(assemblyId);



            var programFromAOI = input.Inspection.Program;
            if (programFromAOI != parentBom)
            {
                throw new Exception("Diferent programs in assembly");
            }


            var units = await BuildInspectionUnitRecords(input);

            await _mESService.AddAttribute(input);

            // 2) Monta o run a partir do input.Inspection (use o mesmo que você já usa nos records)
            var insp = input.Inspection;
            var opInfo = await _mESService.GetOperationInfoAsync(insp?.Barcode);
            var manufacturingArea = opInfo?.ManufacturingArea;

            var run = new InspectionRun
            {
                InspectionBarcode = insp?.Barcode,
                Result = insp?.Result,
                Program = insp?.Program,
                Side = insp?.Side,
                Stencil = insp?.Stencil.ToString(),
                Machine = insp?.Machine,
                User = insp?.User,
                StartTime = ParseDateOffset(insp?.Start),
                EndTime = ParseDateOffset(insp?.End),
                ManufacturingArea = manufacturingArea,
                //Pallet = input.Pallet
                // RawJson = rawJson 
            };

            // 3) Persiste tudo
            var runId = await _repo.SaveSpiRunAsync(run, units);
            return runId;
        }






        public async Task<List<InspectionUnitRecord>> BuildInspectionUnitRecords(SPIInputModel input)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (input.Inspection is null) throw new ArgumentException("Inspection é obrigatório.", nameof(input));

            // 1) Autentica no MES
            var username = Environment.GetEnvironmentVariable("Username");
            var password = Environment.GetEnvironmentVariable("Password");
            await _mESService.SignInAsync(utils.SignInRequest(username, password));

            // 2) Normaliza/deduplica defeitos no input
            SpiDefectUtils.KeepOneDefectPerCRDIgnoringEmptyComp(input);

            // 3) Busca área de manufatura pelo barcode do run
            var operationhistory = await _mESService.GetOperationInfoAsync(input.Inspection.Barcode);
            var manufacturingArea = operationhistory?.ManufacturingArea;

            var remapped = await MapearDefeitosSPICriandoNovo(input);

            // 4) Seleciona um barcode base 
            var baseUnitBarcode = remapped.Board?
                .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Barcode))?.Barcode;

            Dictionary<int, string> positionToSerial = null;

            try
            {
                var wipInfos = await _mESService.GetPanelWipInfoAsync(input.Inspection.Barcode);
                var wipInfo = wipInfos?.FirstOrDefault(w => w.Panel?.PanelWips != null && w.Panel.PanelWips.Any());

                if (wipInfo?.Panel?.PanelWips != null && wipInfo.Panel.PanelWips.Any())
                {
                    positionToSerial = wipInfo.Panel.PanelWips
                    .Where(pw => pw.PanelPosition.HasValue && pw.PanelPosition.Value > 0 && !string.IsNullOrWhiteSpace(pw.SerialNumber))
                    .GroupBy(pw => (int)pw.PanelPosition!.Value)
                    .ToDictionary(g => g.Key, g => g.First().SerialNumber);
                }
            }
            catch (Exception ex)
            {
            }

            var runMeta = remapped.Inspection;
            var units = new List<InspectionUnitRecord>();

            foreach (var b in remapped.Board ?? Enumerable.Empty<Board>())
            {
                var arrayIdx = ParseArrayIndex(b.Array);

                var unitBarcode = !string.IsNullOrWhiteSpace(b.Barcode)
                    ? b.Barcode
                    : (positionToSerial != null && positionToSerial.TryGetValue(arrayIdx, out var serialFromPanel)
                        ? serialFromPanel
                        : DeriveSequentialBarcode(baseUnitBarcode, arrayIdx));



                var record = new InspectionUnitRecord
                {
                    UnitBarcode = unitBarcode,
                    ArrayIndex = arrayIdx,
                    Result = b.Result,
                    Side = runMeta?.Side,
                    Machine = runMeta?.Machine,
                    User = runMeta?.User,
                    StartTime = ParseDate(runMeta?.Start),
                    EndTime = ParseDate(runMeta?.End),
                    ManufacturingArea = manufacturingArea,
                    //Pallet = remapped.Pallet
                };

                var isNg = string.Equals(b.Result, "NG", StringComparison.OrdinalIgnoreCase);
                if (isNg)
                {
                    record.Defects = NormalizeAndDedupDefects(b.Defects);
                }

                units.Add(record);
            }
            return units;
        }



        public static DateTimeOffset? ParseDateOffset(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto;

            // "yyyy/MM/dd HH:mm:ss" em Manaus
            if (DateTime.TryParseExact(s, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtNoZone))
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Manaus");
                var unspecified = DateTime.SpecifyKind(dtNoZone, DateTimeKind.Unspecified);
                return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
            }

            return null;
        }


        private static readonly SemaphoreSlim NormalizeLock = new SemaphoreSlim(1, 1);

        public async Task<SPIInputModel> MapearDefeitosSPICriandoNovo(SPIInputModel spi, CancellationToken ct = default)
        {
            await NormalizeLock.WaitAsync(ct);
            try
            {
                var clone = DeepClone(spi);
                if (clone?.Board == null) return clone;

                var defectMap = await ObterDefectMapAsync();

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



        public async Task<Dictionary<string, string>> ObterDefectMapAsync()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                var query = "SELECT [DEFECTCODE], [DESCRIPTION] FROM DEFECTMAP";

                var defectMap = await connection.QueryAsync<DefectMapEntity>(query);

                return defectMap.ToDictionary(
                    x => x.DEFECTCODE, 
                    x => x.DESCRIPTION,
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }


        public async Task<Dictionary<string, string>> ObterTypeModelMemoryAsync()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                var query = "SELECT [FERT], [SIZE] FROM FERTMAP";

                var defectMap = await connection.QueryAsync<ModelTypeMemory>(query);

                return defectMap.ToDictionary(
                    x => x.FERT,
                    x => x.SIZE,
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }








        public Dictionary<string, string> ObterDefectMap()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                var query = "SELECT [DEFECTCODE], [DESCRIPTION] FROM DEFECTMAP";

                var defectMap = connection.Query<DefectMapEntity>(query);

                return defectMap.ToDictionary(
                    x => x.DEFECTCODE,
                    x => x.DESCRIPTION,
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }





        private static T DeepClone<T>(T obj)
        {
            if (obj == null) return default!;
            var json = JsonSerializer.Serialize(
                obj,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }
            );
            return JsonSerializer.Deserialize<T>(json)!;
        }




        private static string? DeriveSequentialBarcode(string? baseBarcode, int arrayIndex)
        {
            if (string.IsNullOrWhiteSpace(baseBarcode)) return null;
            if (arrayIndex < 1) throw new ArgumentOutOfRangeException(nameof(arrayIndex), "arrayIndex deve ser >= 1");

            var match = Regex.Match(baseBarcode, @"(\d+)$");
            if (!match.Success) return null;

            var digits = match.Groups[1].Value;
            var prefix = baseBarcode[..^digits.Length];

            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return null;

            var newNumber = number + (arrayIndex - 1);
            var padded = newNumber.ToString(new string('0', digits.Length), CultureInfo.InvariantCulture);
            return prefix + padded;
        }

        public static int ParseArrayIndex(int? value)
        {
            return value ?? 0;
        }

        public static DateTime? ParseDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (DateTime.TryParseExact(s, "yyyy/MM/dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                return dt;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                return dt;

            return null;
        }

        public static string NormalizeDefectCode(string? defect, string? review)
        {
            var pick = !string.IsNullOrWhiteSpace(review) ? review : defect;
            return pick?.Trim().Replace(' ', '_').ToUpperInvariant() ?? "";
        }

        public static List<NormalizedDefect> NormalizeAndDedupDefects(List<Defects>? defects)
        {
            var list = new List<NormalizedDefect>();
            if (defects == null || defects.Count == 0) return list;

            var set = new HashSet<string>();
            foreach (var d in defects)
            {
                var code = NormalizeDefectCode(d.Defect, d.Review);
                var comp = string.IsNullOrWhiteSpace(d.Comp) ? null : d.Comp.Trim();
                var part = string.IsNullOrWhiteSpace(d.Part) ? null : d.Part.Trim();
                var key = $"{comp}|{part}|{code}";
                if (set.Add(key))
                {
                    list.Add(new NormalizedDefect
                    {
                        Comp = comp,
                        Part = part,
                        DefectCode = code
                    });
                }
            }
            return list;
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

                // Separa defeitos com Comp vazio dos que têm Comp preenchido
                var defectsWithComp = b.Defects
                    .Where(d => !string.IsNullOrWhiteSpace(d.Comp))
                    .GroupBy(d => d.Comp.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var defectsWithoutComp = b.Defects
                    .Where(d => string.IsNullOrWhiteSpace(d.Comp))
                    .GroupBy(d => $"{d.Part?.Trim()}|{d.Defect?.Trim()}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                // Combina os dois grupos
                b.Defects = defectsWithComp.Concat(defectsWithoutComp).ToList();
            }
        }
    }
    
    
    
}