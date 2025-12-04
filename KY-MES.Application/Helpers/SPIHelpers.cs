using Dapper;
using KY_MES.Application.Exceptions;
using KY_MES.Application.Utils;
using KY_MES.Domain.DefectMap;
using KY_MES.Domain.ModelType;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Services;
using KY_MES.Services.DomainServices.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AppUtils = KY_MES.Application.App.Utils.UtilsModel;
using BomProgramFailException = KY_MES.Application.Exceptions.BomProgramFailException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace KY_MES.Application.Helpers
{
    public class SPIHelpers
    {
        private readonly AppUtils _utils;
        private static readonly SemaphoreSlim NormalizeLock = new SemaphoreSlim(1, 1);
        private readonly IMESService _mESService;
        private readonly IConfiguration _configuration;

        public SPIHelpers(IMESService mESService, IConfiguration configuration)
        {
            _utils = new AppUtils();
            _mESService = mESService;
            _configuration = configuration;
        }

        // ========== Normalização e Mapeamento de Defeitos ==========
        public void KeepOneDefectPerCRDIgnoringEmptyComp(SPIInputModel input)
        {
            if (input?.Board == null) return;

            foreach (var b in input.Board)
            {
                if (b?.Defects == null || b.Defects.Count == 0) continue;

                var withComp = b.Defects
                    .Where(d => !string.IsNullOrWhiteSpace(d.Comp))
                    .GroupBy(d => d.Comp!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var withoutComp = b.Defects
                    .Where(d => string.IsNullOrWhiteSpace(d.Comp))
                    .GroupBy(d => $"{d.Part?.Trim()}|{d.Defect?.Trim()}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                b.Defects = withComp.Concat(withoutComp).ToList();
            }
        }

        //public async Task<SPIInputModel> MapearDefeitosSPICriandoNovo(SPIInputModel spi, CancellationToken ct = default)
        //{
        //    await NormalizeLock.WaitAsync(ct);
        //    try
        //    {
        //        return DeepClone(spi);
        //    }
        //    finally
        //    {
        //        NormalizeLock.Release();
        //    }
        //}

        public async Task<SPIInputModel> MapearDefeitosSPICriandoNovo(SPIInputModel spi, CancellationToken ct = default)
        {
            if (spi is null) throw new ArgumentNullException(nameof(spi));

            var novo = DeepClone(spi);

            var defectMap = await ObterDefectMapAsync(ct);

            if (novo.Board != null)
            {
                foreach (var board in novo.Board)
                {
                    if (board?.Defects == null) continue;

                    foreach (var d in board.Defects)
                    {
                        var code = d?.Defect;
                        if (string.IsNullOrWhiteSpace(code)) continue;

                        if (defectMap.TryGetValue(code.Trim(), out var mappedDesc) &&
                            !string.IsNullOrWhiteSpace(mappedDesc))
                        {
                            d.Defect = mappedDesc;
                        }
                    }
                }
            }
            return novo;
        }

        // ========== Validações ==========

        public async Task ValidateSizeGbIfNeeded(SPIInputModel input, int wipId, IMESService? mesOverride = null)
        {
            if (input?.Inspection == null)
                throw new ArgumentException("Inspection inválido no input");

            var program = input.Inspection.Program ?? string.Empty;

            // Só valida se aparecer "GB" no program (case-insensitive)
            if (program.IndexOf("GB", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            // Extrai size do Program usando o mesmo padrão do legado: "-(ddd)(GB)?-"
            var sizeFromProgram = ExtractAndNormalizeSizeFromProgram(program);
            if (string.IsNullOrEmpty(sizeFromProgram))
                return; // Sem size parseável no Program -> mantém comportamento atual (sai silenciosamente)

            var mes = mesOverride ?? _mESService ?? throw new ArgumentNullException(nameof(mesOverride), "IMESService indisponível para validação de SIZE/GB.");

            // Busca o FERT/BOM no MES
            var assemblyId = await mes.GetAssemblyId(wipId);
            var fert = await mes.GetProgramInBomSPI(assemblyId);
            if (string.IsNullOrWhiteSpace(fert))
                throw new FertSpiException("FERT do BOM do SPI não retornado pelo MES.");

            // Carrega dicionário de modelo->size, mantendo a mesma regra anterior
            var assemblyModelMemory = await ObterTypeModelMemoryAsync();

            if (!assemblyModelMemory.TryGetValue(fert, out var sizeFromDB))
                throw new FertSpiException($"FERT {fert} não encontrado no dicionário");

            // Normaliza size do dicionário também
            var sizeFromDictionary = NormalizeSize(sizeFromDB);

            // Compara normalizado (ex.: "128G")
            if (!sizeFromDictionary.Equals(sizeFromProgram, StringComparison.OrdinalIgnoreCase))
                throw new SizeException($"Size não corresponde. Esperado: {sizeFromDictionary}, Recebido: {sizeFromProgram}");
        }

        private static string ExtractAndNormalizeSizeFromProgram(string program)
        {
            if (string.IsNullOrEmpty(program))
                return string.Empty;

            // Mesmo regex do legado, com case-insensitive
            var match = System.Text.RegularExpressions.Regex.Match(
                program,
                @"-(\d{3})(?:GB)?-",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (!match.Success)
                return string.Empty;

            var numeric = match.Groups[1].Value; // "128", "256", etc.
            return NormalizeSize(numeric);
        }

        private static string NormalizeSize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var cleaned = raw.Trim().ToUpperInvariant();

            // Aceita "128", "128G", "128GB"; normaliza para "128G"
            cleaned = cleaned.Replace("GB", "", StringComparison.OrdinalIgnoreCase).Replace("G", "", StringComparison.OrdinalIgnoreCase).Trim();

            // Garante 3 dígitos se for numérico de 2-3 dígitos (ajuste se precisar)
            // Opcional: validar faixas esperadas (064, 128, 256, 512)
            return $"{cleaned}G";
        }

        public async Task ValidateProgramVsBomOrBotForAOI(SPIInputModel input, int wipId, IMESService mes)
        {
            if (input?.Inspection == null)
                throw new ArgumentException("Inspection inválido no input");

            var program = input.Inspection.Program ?? string.Empty;

            // 1) Pega o AssemblyId pelo WipId
            var assemblyId = await mes.GetAssemblyId(wipId);

            // 2) Busca o “ParentBomName” para comparação
            var parentBom = await mes.GetProgramInBom(assemblyId);

            // 3) Regra: aceitar se program == parentBom (case-insensitive) OU se o program contiver “BOT”
            var equalsBom = program.Equals(parentBom, StringComparison.OrdinalIgnoreCase);
            var containsBOT = program.IndexOf("BOT", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!(equalsBom || containsBOT))
            {
                throw new BomProgramFailException("Program is different for this product");
            }
        }



        public async Task ValidateProgramEqualsBomStrict(SPIInputModel input, int wipId, IMESService mes)
        {
            var program = input?.Inspection?.Program ?? string.Empty;
            var assemblyId = await mes.GetAssemblyId(wipId);
            var parentBom = await mes.GetProgramInBom(assemblyId);

            if (!program.Equals(parentBom, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Diferent programs in assembly");
        }

        public async Task ValidateProgramEqualsBomStrict(SPIInputModel input, int wipId)
        {
            await Task.CompletedTask;
        }

        // ========== Rework ==========
        public string BuildResourceMachine(string? manufacturingArea, string suffix)
        {
            if (string.IsNullOrWhiteSpace(manufacturingArea)) return suffix;
            var last = manufacturingArea.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return string.IsNullOrWhiteSpace(last) ? suffix : $"{last} {suffix}";
        }

        public async Task ExecutarReworkSeNecessario(
            IMESService mes,
            IEnumerable<WipSerial> wipIdInts,
            string resourceMachine,
            int wipPrincipal)
        {
            foreach (var wip in wipIdInts)
            {
                var indictmentIds = await mes.GetIndictmentIds(wip.WipId);
                if (indictmentIds.Count > 0)
                {
                    await mes.OkToStartRework(wip.WipId, resourceMachine!, wip.SerialNumber);
                    foreach (var indictmentId in indictmentIds)
                    {
                        await mes.AddRework(wip.WipId, indictmentId);
                    }
                    await mes.CompleteRework(wipPrincipal);
                }
            }
        }

        // ========== WIP mappers e retry (via Utils) ==========
        public GetWipIdBySerialNumberRequestModel SpiToGetWip(SPIInputModel input) => _utils.SpiToGetWip(input);
        public OkToStartRequestModel ToOkToStart(SPIInputModel input, GetWipIdBySerialNumberResponseModels getWip) => _utils.ToOkToStart(input, getWip);
        public StartWipRequestModel ToStartWip(SPIInputModel input, GetWipIdBySerialNumberResponseModels getWip) => _utils.ToStartWip(input, getWip);
        public AddDefectRequestModel ToAddDefect(SPIInputModel input, GetWipIdBySerialNumberResponseModels getWip) => _utils.ToAddDefect(input, getWip);
        public SignInRequestModel SignInRequest(string username, string password) => _utils.SignInRequest(username, password);

        public async Task<CompleteWipResponseModel?> TryAddDefectWithRetry(
            Func<Task<AddDefectResponseModel>> addDefectCallFromMes,
            Func<Task<CompleteWipResponseModel>> completeWipAfterAdd,
            int maxRetries,
            int delayMs)
        {

            int retry = 0;
            while (true)
            {
                try
                {
                    await addDefectCallFromMes();
                    var complete = await completeWipAfterAdd();
                    return complete;
                }
                catch
                {
                    retry++;
                    if (retry >= maxRetries) throw;
                    await Task.Delay(delayMs);
                }
            }
        }
        public async Task<CompleteWipResponseModel?> TryAddDefectWithRetry(
            Func<Task<AddDefectResponseModel>> addDefectCallFromMes,
            int maxRetries,
            int delayMs)
        {
            int retry = 0;
            while (true)
            {
                try
                {
                    await addDefectCallFromMes();
                    return new CompleteWipResponseModel();
                }
                catch
                {
                    retry++;
                    if (retry >= maxRetries) throw;
                    await Task.Delay(delayMs);
                }
            }
        }

        public bool IsSPIMachine(string? machine)
        {
            if (string.IsNullOrWhiteSpace(machine)) return false;
            return machine.StartsWith("SS-DL", StringComparison.OrdinalIgnoreCase)
                || machine.StartsWith("SP-DL", StringComparison.OrdinalIgnoreCase);
        }

        // ========== Builders ==========
        public async Task<List<InspectionUnitRecord>> BuildInspectionUnitRecords(
            SPIInputModel input,
            OperationInfo operationhistory,
            IMESService mes)
        {
            var manufacturingArea = operationhistory?.ManufacturingArea;

            var baseUnitBarcode = input.Board?
                .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Barcode))?.Barcode;

            Dictionary<int, string>? positionToSerial = null;

            try
            {
                var wipInfos = await mes.GetPanelWipInfoAsync(input.Inspection.Barcode);
                var wipInfo = wipInfos?.FirstOrDefault(w => w.Panel?.PanelWips != null && w.Panel.PanelWips.Any());

                if (wipInfo?.Panel?.PanelWips != null && wipInfo.Panel.PanelWips.Any())
                {
                    positionToSerial = wipInfo.Panel.PanelWips
                        .Where(pw => pw.PanelPosition.HasValue && pw.PanelPosition.Value > 0 && !string.IsNullOrWhiteSpace(pw.SerialNumber))
                        .GroupBy(pw => (int)pw.PanelPosition!.Value)
                        .ToDictionary(g => g.Key, g => g.First().SerialNumber);
                }
            }
            catch
            {
                // manter silencioso como no comportamento atual
            }

            var runMeta = input.Inspection;
            var units = new List<InspectionUnitRecord>();

            foreach (var b in input.Board ?? Enumerable.Empty<Board>())
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
                    //Carrier = ""
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

        public InspectionRun BuildInspectionRun(SPIInputModel input, string? manufacturingArea)
        {
            var insp = input.Inspection;

            return new InspectionRun
            {
                InspectionBarcode = insp?.Barcode,
                Result = insp?.Result,
                Program = insp?.Program,
                Side = insp?.Side,
                Stencil = insp?.Stencil?.ToString(),
                Machine = insp?.Machine,
                User = insp?.User,
                StartTime = ParseDateOffset(insp?.Start),
                EndTime = ParseDateOffset(insp?.End),
                ManufacturingArea = manufacturingArea,
                //Carrier = "",
                RawJson = JsonConvert.SerializeObject(input, Formatting.None)
            };
        }

        // ========== Utilitários ==========
        public static int ParseArrayIndex(int? value) => value ?? 0;

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

        private static T DeepClone<T>(T obj)
        {
            if (obj == null) return default!;
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

        public async Task<Dictionary<string, string>> ObterDefectMapAsync(CancellationToken ct = default)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            const string query = @"
            SELECT [DEFECTCODE], [DESCRIPTION]
            FROM [KY-MES].[dbo].[DEFECTMAP]";

            var rows = await connection.QueryAsync<DefectMapEntity>(new CommandDefinition(query, cancellationToken: ct));

            var dict = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.DEFECTCODE))
                .ToDictionary(
                    x => x.DEFECTCODE.Trim(),
                    x => (x.DESCRIPTION ?? string.Empty).Trim(),
                    StringComparer.OrdinalIgnoreCase
                );

            return dict;
        }



    }
}