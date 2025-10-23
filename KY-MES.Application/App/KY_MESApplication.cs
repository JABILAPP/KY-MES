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
using KY_MES.Application;

namespace KY_MES.Controllers
{
    public class KY_MESApplication : IKY_MESApplication
    {
        private readonly IMESService _mESService;
        private readonly Utils utils;
        private readonly ISpiRepository _repo;
        private readonly IConfiguration _configuration;

        private static Dictionary<string, string>? _cachedDefectMap;
        private static Dictionary<string, string>? _cachedTypeModelMemory;
        private static DateTime _defectMapCacheTime = DateTime.MinValue;
        private static DateTime _typeModelCacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);
        private static readonly SemaphoreSlim CacheLock = new SemaphoreSlim(1, 1);

        public KY_MESApplication(IMESService mESService, ISpiRepository repo, IConfiguration configuration)
        {
            _mESService = mESService;
            utils = new Utils();
            _repo = repo;
            _configuration = configuration;
        }

        public async Task<long> SPISendWipData(SPIInputModel sPIInput)
        {

            //var username = Environment.GetEnvironmentVariable("Username");
            //var password = Environment.GetEnvironmentVariable("Password");
            //await _mESService.SignInAsync(utils.SignInRequest(username, password));


            // normaliza defeitos
            SpiDefectUtils.KeepOneDefectPerCRDIgnoringEmptyComp(sPIInput);


            var operationHistoryTask = _mESService.GetOperationInfoAsync(sPIInput.Inspection.Barcode);
            var remapTask = MapearDefeitosSPICriandoNovo(sPIInput);
            var getWipTask = _mESService.GetWipIdBySerialNumberAsync(utils.SpiToGetWip(sPIInput));

            await Task.WhenAll(remapTask, getWipTask);

            var operationhistory = await operationHistoryTask;
            var sPIInputRemapped = await remapTask;
            var getWipResponse = await getWipTask;

            if (getWipResponse.WipId == null)
                throw new Exception("WipId não encontrado");

            var serialNumber = sPIInput.Inspection.Barcode;
            var wipPrincipal = getWipResponse.WipId;

            //GetWipIds + GetAssemblyId
            var wipIdsTask = _mESService.GetWipIds(serialNumber!);
            var assemblyIdTask = _mESService.GetAssemblyId(wipPrincipal);

            await Task.WhenAll(wipIdsTask, assemblyIdTask);

            var wipIdInts = await wipIdsTask;
            var assemblyId = await assemblyIdTask;

            // GetProgramInBom + GetProgramInBomSPI
            var parentBomTask = _mESService.GetProgramInBom(assemblyId);
            var parentBomSPITask = _mESService.GetProgramInBomSPI(assemblyId);

            await Task.WhenAll(parentBomTask, parentBomSPITask);

            var parentBom = await parentBomTask;
            var parentBomSPI = await parentBomSPITask;

            // Validação de assembly para spi
            var programFromSPI = sPIInputRemapped.Inspection.Program;

            if (programFromSPI.Contains("GB"))
            {
                var assemblyModelMemory = await ObterTypeModelMemoryAsync();

                if (!assemblyModelMemory.TryGetValue(parentBomSPI, out var sizeFromDB))
                {
                    throw new FertSpiException($"FERT {parentBomSPI} não encontrado no dicionário");
                }

                var sizeMatch = Regex.Match(programFromSPI, @"-(\d{3})(?:GB)?-");
                if (sizeMatch.Success)
                {
                    var sizeFromSPI = sizeMatch.Groups[1].Value + "G";

                    if (sizeFromDB != sizeFromSPI)
                    {
                        throw new SizeException($"Size não corresponde. Esperado: {sizeFromDB}, Recebido: {sizeFromSPI}");
                    }
                }
            }

            CompleteWipResponseModel? completeWipResponse = null;

            string resourceMachine = CalculateResourceMachine(operationhistory.ManufacturingArea);

            // Processa rework 
            if (sPIInputRemapped.Inspection.Result.Contains("NG"))
            {
                if (sPIInputRemapped.Inspection.Machine.StartsWith("SS-DL") || sPIInputRemapped.Inspection.Machine.StartsWith("SP-DL"))
                {
                    await ProcessReworkForAllWips(wipIdInts, wipPrincipal, resourceMachine);
                    completeWipResponse = await ProcessNGFlow(sPIInputRemapped, getWipResponse);
                }
                else
                {
                    var programFromAOI = sPIInputRemapped.Inspection.Program;
                    if (programFromAOI == parentBom)
                    {
                        await ProcessReworkForAllWips(wipIdInts, wipPrincipal, resourceMachine);
                        completeWipResponse = await ProcessNGFlow(sPIInputRemapped, getWipResponse);
                    }
                    else
                    {
                        throw new Exception("Program is different for this product");
                    }
                }
            }
            else // PASS
            {
                if (sPIInputRemapped.Inspection.Machine.StartsWith("SS-DL")|| sPIInputRemapped.Inspection.Machine.StartsWith("SP-DL"))
                {
                    await ProcessReworkForAllWips(wipIdInts, wipPrincipal, resourceMachine);
                    completeWipResponse = await ProcessPassFlow(sPIInputRemapped, getWipResponse);
                }
                else
                {
                    var programFromAOI = sPIInputRemapped.Inspection.Program;

                    if (programFromAOI.Contains("BOT") || programFromAOI == parentBom)
                    {
                        await ProcessReworkForAllWips(wipIdInts, wipPrincipal, resourceMachine);
                        completeWipResponse = await ProcessPassFlow(sPIInputRemapped, getWipResponse);
                    }
                    else
                    {
                        throw new BomProgramFailException("Program is different for this product");
                    }
                }
            }

            if (completeWipResponse == null)
                throw new CompleteWipException("complete wip failed");

            // Salva no banco
            try
            {
                var units = await BuildInspectionUnitRecords(sPIInputRemapped);
                var manufacturingArea = operationhistory?.ManufacturingArea;

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
                };

                var runId = await _repo.SaveSpiRunAsync(run, units);
                return runId;
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        private string CalculateResourceMachine(string? manufacturingArea)
        {
            string suffix = "- Repair 01";
            return string.IsNullOrWhiteSpace(manufacturingArea)
                ? suffix
                : $"{manufacturingArea.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()} {suffix}";
        }

        private async Task ProcessReworkForAllWips(List<WipSerial> wipIdInts, int wipPrincipal, string resourceMachine)
        {
            // Busca todos os indictmentIds em paralelo primeiro
            var indictmentTasks = wipIdInts.Select(async wip =>
            {
                try
                {
                    var indictmentIds = await _mESService.GetIndictmentIds(wip.WipId);
                    return (Wip: wip, IndictmentIds: indictmentIds);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao buscar indictments para WipId {wip.WipId}: {ex.Message}");
                    return (Wip: wip, IndictmentIds: new List<int>());
                }
            }).ToList();

            var results = await Task.WhenAll(indictmentTasks);

            // Processa apenas os que têm indictments
            var wipsWithIndictments = results.Where(r => r.IndictmentIds.Count > 0).ToList();

            if (wipsWithIndictments.Count == 0)
                return;

            // Processa todos em paralelo
            var reworkTasks = wipsWithIndictments.Select(async item =>
            {
                try
                {
                    await _mESService.OkToStartRework(item.Wip.WipId, resourceMachine, item.Wip.SerialNumber);

                    // Processa AddRework em paralelo
                    var addReworkTasks = item.IndictmentIds.Select(indictmentId =>
                        _mESService.AddRework(item.Wip.WipId, indictmentId)
                    );

                    await Task.WhenAll(addReworkTasks);

                    await _mESService.CompleteRework(wipPrincipal);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar rework para WipId {item.Wip.WipId}: {ex.Message}");
                }
            });

            await Task.WhenAll(reworkTasks);
        }

        // Método auxiliar para fluxo NG
        private async Task<CompleteWipResponseModel?> ProcessNGFlow(SPIInputModel input, GetWipIdBySerialNumberResponseModels wipResponse)
        {
            var okToTestResponse = await _mESService.OkToStartAsync(utils.ToOkToStart(input, wipResponse));
            if (okToTestResponse == null || !okToTestResponse.OkToStart)
                throw new CheckPVFailedException("Check PV failed");

            var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(input, wipResponse));
            if (startWipResponse == null || !startWipResponse.Success)
                throw new StartWipException("start Wip failed");

            CompleteWipResponseModel? completeWipResponse = null;
            int retryCount = 0;
            int maxRetries = 10;

            do
            {
                try
                {
                    completeWipResponse = await utils.AddDefectToCompleteWip(
                        _mESService.AddDefectAsync(
                            utils.ToAddDefect(input, wipResponse),
                            wipResponse.WipId
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

            return completeWipResponse;
        }

        // Método auxiliar para fluxo PASS
        private async Task<CompleteWipResponseModel> ProcessPassFlow(SPIInputModel input, GetWipIdBySerialNumberResponseModels wipResponse)
        {
            var okToTestResponse = await _mESService.OkToStartAsync(utils.ToOkToStart(input, wipResponse));
            if (okToTestResponse == null || !okToTestResponse.OkToStart)
                throw new CheckPVFailedException("Check PV failed");

            var startWipResponse = await _mESService.StartWipAsync(utils.ToStartWip(input, wipResponse));
            if (startWipResponse == null || !startWipResponse.Success)
                throw new StartWipException("start Wip failed");

            return await _mESService.CompleteWipPassAsync(
                utils.ToCompleteWipPass(input, wipResponse),
                wipResponse.WipId.ToString()
            );
        }

        public async Task<long> SPISendWipDataLog(SPIInputModel input)
        {
            // Paraleliza: GetWipId
            var getWipTask = _mESService.GetWipIdBySerialNumberAsync(utils.SpiToGetWip(input));
            var getWipResponse = await getWipTask;
            var WipId = getWipResponse.WipId;

            // Paraleliza: GetAssemblyId + BuildInspectionUnitRecords + GetOperationInfo
            var assemblyIdTask = _mESService.GetAssemblyId(WipId);
            var unitsTask = BuildInspectionUnitRecords(input);
            var opInfoTask = _mESService.GetOperationInfoAsync(input.Inspection?.Barcode);

            await Task.WhenAll(assemblyIdTask, unitsTask, opInfoTask);

            var assemblyId = await assemblyIdTask;
            var parentBom = await _mESService.GetProgramInBom(assemblyId);

            var programFromAOI = input.Inspection.Program;
            if (programFromAOI != parentBom)
            {
                throw new Exception("Diferent programs in assembly");
            }

            // AddAttribute em paralelo com preparação do run
            var addAttributeTask = _mESService.AddAttribute(input);

            var units = await unitsTask;
            var opInfo = await opInfoTask;
            var manufacturingArea = opInfo?.ManufacturingArea;

            var insp = input.Inspection;
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
            };

            await addAttributeTask;

            var runId = await _repo.SaveSpiRunAsync(run, units);
            return runId;
        }

        public async Task<List<InspectionUnitRecord>> BuildInspectionUnitRecords(SPIInputModel input)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (input.Inspection is null) throw new ArgumentException("Inspection é obrigatório.", nameof(input));

            // Normaliza defeitos
            SpiDefectUtils.KeepOneDefectPerCRDIgnoringEmptyComp(input);

            // Paraleliza: OperationHistory + Remapeamento + GetPanelWipInfo
            var operationHistoryTask = _mESService.GetOperationInfoAsync(input.Inspection.Barcode);
            var remappedTask = MapearDefeitosSPICriandoNovo(input);
            var wipInfoTask = _mESService.GetPanelWipInfoAsync(input.Inspection.Barcode);

            await Task.WhenAll(operationHistoryTask, remappedTask, wipInfoTask);

            var operationhistory = await operationHistoryTask;
            var manufacturingArea = operationhistory?.ManufacturingArea;
            var remapped = await remappedTask;
            var wipInfos = await wipInfoTask;

            var baseUnitBarcode = remapped.Board?
                .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Barcode))?.Barcode;

            Dictionary<int, string>? positionToSerial = null;

            try
            {
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
                // Ignora erro
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

            if (DateTime.TryParseExact(s, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtNoZone))
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Manaus");
                var unspecified = DateTime.SpecifyKind(dtNoZone, DateTimeKind.Unspecified);
                return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
            }

            return null;
        }

        public async Task<SPIInputModel> MapearDefeitosSPICriandoNovo(SPIInputModel spi, CancellationToken ct = default)
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

        public async Task<Dictionary<string, string>> ObterDefectMapAsync()
        {
            // Verifica cache
            if (_cachedDefectMap != null && DateTime.UtcNow - _defectMapCacheTime < CacheExpiration)
            {
                return _cachedDefectMap;
            }

            await CacheLock.WaitAsync();
            try
            {
                // Double-check após lock
                if (_cachedDefectMap != null && DateTime.UtcNow - _defectMapCacheTime < CacheExpiration)
                {
                    return _cachedDefectMap;
                }

                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    var query = "SELECT [DEFECTCODE], [DESCRIPTION] FROM DEFECTMAP";

                    var defectMap = await connection.QueryAsync<DefectMapEntity>(query);

                    _cachedDefectMap = defectMap.ToDictionary(
                        x => x.DEFECTCODE,
                        x => x.DESCRIPTION,
                        StringComparer.OrdinalIgnoreCase
                    );

                    _defectMapCacheTime = DateTime.UtcNow;

                    return _cachedDefectMap;
                }
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public async Task<Dictionary<string, string>> ObterTypeModelMemoryAsync()
        {
            // Verifica cache
            if (_cachedTypeModelMemory != null && DateTime.UtcNow - _typeModelCacheTime < CacheExpiration)
            {
                return _cachedTypeModelMemory;
            }

            await CacheLock.WaitAsync();
            try
            {
                // Double-check após lock
                if (_cachedTypeModelMemory != null && DateTime.UtcNow - _typeModelCacheTime < CacheExpiration)
                {
                    return _cachedTypeModelMemory;
                }

                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    var query = "SELECT [FERT], [SIZE] FROM FERTMAP";

                    var defectMap = await connection.QueryAsync<ModelTypeMemory>(query);

                    _cachedTypeModelMemory = defectMap.ToDictionary(
                        x => x.FERT,
                        x => x.SIZE,
                        StringComparer.OrdinalIgnoreCase
                    );

                    _typeModelCacheTime = DateTime.UtcNow;

                    return _cachedTypeModelMemory;
                }
            }
            finally
            {
                CacheLock.Release();
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

                b.Defects = defectsWithComp.Concat(defectsWithoutComp).ToList();
            }
        }
    }
}