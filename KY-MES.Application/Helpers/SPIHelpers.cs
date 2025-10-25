using KY_MES.Application.Exceptions;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Services.DomainServices.Interfaces;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KY_MES.Application.Utils;
using AppUtils = KY_MES.Application.App.Utils.Utils;

namespace KY_MES.Application.Helpers
{
    public class SPIHelpers
    {
        private readonly AppUtils _utils;
        private static readonly SemaphoreSlim NormalizeLock = new SemaphoreSlim(1, 1);

        public SPIHelpers()
        {
            _utils = new AppUtils();
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

        public async Task<SPIInputModel> MapearDefeitosSPICriandoNovo(SPIInputModel spi, CancellationToken ct = default)
        {
            await NormalizeLock.WaitAsync(ct);
            try
            {
                return DeepClone(spi);
            }
            finally
            {
                NormalizeLock.Release();
            }
        }

        // ========== Validações ==========

        public async Task ValidateSizeGbIfNeeded(SPIInputModel input, int wipId, IMESService? mesOverride = null)
        {
            if (input?.Inspection == null)
                throw new ArgumentException("Inspection inválido no input");

            var program = input.Inspection.Program ?? string.Empty;

            // Só valida se aparecer GB no program
            if (program.IndexOf("GB", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            var sizeFromProgram = ExtractSizeFromProgram(program);
            if (string.IsNullOrEmpty(sizeFromProgram))
                return; 

            var mes = mesOverride ?? throw new ArgumentNullException(nameof(mesOverride), "Forneça IMESService para validação de SIZE/GB.");

            // Busca o FERT (programa do SPI no BOM) via MES
            var assemblyId = await mes.GetAssemblyId(wipId);
            var fert = await mes.GetProgramInBomSPI(assemblyId) ?? string.Empty;

            var sizeFromFert = ExtractSizeFromFert(fert);

            // Regras:
            // - Se os dois sizes forem extraídos e diferentes -> erro
            // - Se não deu pra extrair do FERT -> passa (comportamento "soft", ajustável)
            if (!string.IsNullOrEmpty(sizeFromFert) && !string.Equals(sizeFromProgram, sizeFromFert, StringComparison.OrdinalIgnoreCase))
            {
                throw new SizeException($"Program size mismatch: SPI={sizeFromProgram}G vs FERT={sizeFromFert}G");
            }
        }

        private static string ExtractSizeFromProgram(string program)
        {
            // Captura números de "GB" (128GB, 256GB)
            var m = Regex.Match(program, @"[-_\. ](\d{2,3})\s*GB\b", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(program, @"\b(\d{2,3})\s*GB\b", RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value : string.Empty;
        }


        private static string ExtractSizeFromFert(string fert)
        {
            if (string.IsNullOrWhiteSpace(fert))
                return string.Empty;

            var m = Regex.Match(fert, @"\b(\d{2,3})\s*G[B]?\b", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(fert, @"[-_\. ](\d{2,3})(?:\D|$)", RegexOptions.IgnoreCase);

            return m.Success ? m.Groups[1].Value : string.Empty;
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
    }
}