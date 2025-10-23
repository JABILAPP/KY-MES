using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Services.DomainServices.Interfaces;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;
using KY_MES.Domain.V1.DTOs.AddAttributeModel;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using KY_MES.Services.Exceptions;

namespace KY_MES.Services
{
    public class MESService : IMESService
    {
        public static string MesBaseUrl = Environment.GetEnvironmentVariable("MES_BASE_URL");
        private readonly CookieContainer _cookieContainer;
        private readonly HttpClientHandler _handler;
        private readonly HttpClient _client;

        private readonly IConfiguration _configuration;

        private readonly ConcurrentDictionary<int, List<int>> _indictmentsByWip = new ConcurrentDictionary<int, List<int>>();
        private readonly ConcurrentDictionary<string, List<int>> _wipIdsBySerial = new ConcurrentDictionary<string, List<int>>();


        public MESService(IConfiguration configuration)
        {
            _configuration = configuration;
            _cookieContainer = new CookieContainer();
            _handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer
            };
            _client = new HttpClient(_handler);

            var token = GetUserTokenFromDb();
            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(MesBaseUrl))
            {
                _cookieContainer.Add(new Uri(MesBaseUrl), new Cookie("UserToken", token));
            }
        }

        private string GetUserTokenFromDb()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            using var connection = new SqlConnection(connectionString);
            connection.Open();

            var cmd = new SqlCommand("SELECT TOP 1 [UserToken] FROM [KY-MES].[dbo].[MesUserTokenLog] ORDER BY [LastUpdated] DESC", connection);
            var token = cmd.ExecuteScalar() as string;
            return token ?? string.Empty;
        }


        public async Task SignInAsync(SignInRequestModel signInRequestModel)
        {
            try
            {
                //Setting the credentials to Basic Auth
                var byteArray = Encoding.ASCII.GetBytes($"{signInRequestModel.Username}:{signInRequestModel.Password}");
                var base64Credentials = Convert.ToBase64String(byteArray);

                var signInUrl = MesBaseUrl + @"api-external-api/api/user/adsignin";

                // Set the Basic Authentication header
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);

                var response = await _client.GetAsync(signInUrl);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();

                //Add token to the cookie container
                _cookieContainer.Add(new Uri(MesBaseUrl), new Cookie("UserToken", responseBody.Split('=')[1].Split(';')[0]));
            }
            catch (Exception ex)
            {
                throw new SingInException($"Erro ao tentar o login com as credenciais fornecidas da conta de serviço");
            }
        }


        public async Task<GetWipIdBySerialNumberResponseModels> GetWipIdBySerialNumberAsync(GetWipIdBySerialNumberRequestModel getWipIdRequestModel)
        {
            try
            {
                var getWipUrl = $"{MesBaseUrl}api-external-api/api/Wips/GetWipIdBySerialNumber";
                var requestUrl = $"{getWipUrl}?SiteName={getWipIdRequestModel.SiteName}&SerialNumber={getWipIdRequestModel.SerialNumber}";

                var response = await _client.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var responseModel = JsonConvert.DeserializeObject<List<GetWipIdBySerialNumberResponseModels>>(responseBody)[0];
                return responseModel;
            }
            catch (Exception ex) { throw new WipIdException($"Erro ao coletar o WipId. Mensagem: {ex.Message}"); }

        }


        public async Task<OkToStartResponseModel> OkToStartAsync(OkToStartRequestModel okToStartRequestModel)
        {
            try
            {
                var okToStartUrl = $"{MesBaseUrl}api-external-api/api/Wips/{okToStartRequestModel.WipId}/oktostart?resourceName={okToStartRequestModel.ResourceName}";

                var response = await _client.GetAsync(okToStartUrl);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var responseModel = JsonConvert.DeserializeObject<OkToStartResponseModel>(responseBody);
                return responseModel;

            }
            catch (Exception ex)
            {

                throw new CheckPvException($"Erro ao fazer o Check PV. Mensagem: {ex.Message}");
            }
        }



        public async Task<StartWipResponseModel> StartWipAsync(StartWipRequestModel startWipRequestModel)
        {
            try
            {
                var startWipUrl = $"{MesBaseUrl}api-external-api/api/PanelWip/startWip";

                var jsonContent = JsonConvert.SerializeObject(startWipRequestModel);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(startWipUrl, content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var responseModel = JsonConvert.DeserializeObject<StartWipResponseModel>(responseBody);
                return responseModel;
            }
            catch (Exception ex)
            {
                throw new StartWipException($"Erro ao executar StartWip. Mensagem: {ex.Message}");
            }
        }


        public async Task<CompleteWipResponseModel> CompleteWipFailAsync(CompleteWipFailRequestModel completWipRequestModel, string WipId)
        {
            try
            {
                var completeWipUrl = $"{MesBaseUrl}api-external-api/api/Wips/{WipId}/complete";

                var jsonContent = JsonConvert.SerializeObject(completWipRequestModel);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(completeWipUrl, content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var responseModel = JsonConvert.DeserializeObject<CompleteWipResponseModel>(responseBody);
                return responseModel;
            }
            catch (Exception ex)
            {
                throw new CompleteWipFailException($"Erro ao executar CompleteWipFail. Mensagem: {ex.Message}");
            }
        }

        public async Task<AddDefectResponseModel> AddDefectAsync(AddDefectRequestModel addDefectRequestModel, int WipId)
        {
            try
            {
                var addDefectUrl = $"{MesBaseUrl}api-external-api/api/Wips/{WipId}/AddDefects";

                // 1) Primeira tentativa: dedup por defectCRD + defectName (mantém nomes originais)
                if (addDefectRequestModel?.panelDefects != null)
                {
                    foreach (var panel in addDefectRequestModel.panelDefects)
                    {
                        if (panel?.defects != null)
                        {
                            panel.defects = panel.defects
                                .GroupBy(d => new
                                {
                                    Comp = (d.defectCRD ?? string.Empty).Trim(),
                                    Defect = (d.defectName ?? string.Empty).Trim(),
                                })
                                .Select(g => g.First())
                                .ToList();
                        }
                    }
                }

                var jsonContent = JsonConvert.SerializeObject(addDefectRequestModel);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(addDefectUrl, content);

                // 2) Segunda tentativa trocando o crd para HALBIM
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {

                    if (addDefectRequestModel?.panelDefects != null)
                    {
                        foreach (var panel in addDefectRequestModel.panelDefects)
                        {
                            if (panel?.defects == null) continue;

                            foreach (var d in panel.defects)
                            {
                                if (d == null) continue;
                                d.defectCRD = "HALBIM";
                                d.defectComment = "HALBIM";
                            }

                            panel.defects = panel.defects
                                .GroupBy(d => new
                                {
                                    Comp = (d.defectCRD ?? string.Empty).Trim(),
                                    Defect = (d.defectName ?? string.Empty).Trim(),
                                })
                                .Select(g => g.First())
                                .ToList();
                        }
                    }

                    jsonContent = JsonConvert.SerializeObject(addDefectRequestModel);
                    content = new StringContent(jsonContent, Encoding.UTF8, "application/json");


                    response = await _client.PostAsync(addDefectUrl, content);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    throw new AddDefectException($"Erro ao executar AddDefect (COMPONENTES E NEM HALMBIM ESTÁ SENDO ACEITO PELO MES)");
                }

                await CompleteWipIoTAsync(WipId);

                return new AddDefectResponseModel();
            }
            catch (Exception ex)
            {
                throw new AddDefectException($"Erro ao executar AddDefect. Mensagem: {ex.Message}");
            }
        }

        public async Task CompleteWipIoTAsync(int wipId)
        {
            try
            {
                var completeWipIoTUrl = $"{MesBaseUrl}api-external-api/api/Wips/{wipId}/complete";

                var completeWipIoTRequestModel = new CompleteWipIoTRequestModel
                {
                    WipId = wipId,
                };

                var jsonContent = JsonConvert.SerializeObject(completeWipIoTRequestModel);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(completeWipIoTUrl, content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao executar o completeWipIoT. Mensagem: {ex.Message}");
            }
        }

        public async Task<CompleteWipResponseModel> CompleteWipPassAsync(CompleteWipPassRequestModel completWipRequestModel, string WipId)
        {
            try
            {
                var completeWipUrl = $"{MesBaseUrl}api-external-api/api/Wips/{WipId}/complete";

                var jsonContent = JsonConvert.SerializeObject(completWipRequestModel);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(completeWipUrl, content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var responseModel = JsonConvert.DeserializeObject<CompleteWipResponseModel>(responseBody);
                return responseModel;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao executar CompleteWipPass. Mensagem: {ex.Message}");
            }
        }



        #region New Methods for automatic rework to retest the board in production line 
        #endregion
        public async Task<List<int>> GetIndictmentIds(int wipId)
        {
            try
            {
                var listDefectUrl = $"{MesBaseUrl}api-external-api/api/Wips/ListDefectsByWipId?WipId={wipId}&OnlyOpenDefects=true";

                var getResponse = await _client.GetAsync(listDefectUrl);

                getResponse.EnsureSuccessStatusCode();

                var getBody = await getResponse.Content.ReadAsStringAsync();


                var defects = JsonConvert.DeserializeObject<List<DefectDtoMin>>(getBody) ?? new List<DefectDtoMin>();

                var indictmentIds = defects
                    .Where(d => d.IndictmentId.HasValue && d.IndictmentId.Value > 0)
                    .Select(d => d.IndictmentId!.Value)
                    .Distinct()
                    .ToList();

                _indictmentsByWip[wipId] = indictmentIds;

                return indictmentIds;

            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao pegar IndictmentId: {ex.Message}");
            }
        }

        public async Task<List<WipSerial>> GetWipIds(string serialNumber)
        {
            try
            {
                var wipIdsUrl = $"{MesBaseUrl}api-external-api/api/Wips/GetWipIdBySerialNumber?SiteName=MANAUS&SerialNumber={serialNumber}";

                var getResponse = await _client.GetAsync(wipIdsUrl);
                getResponse.EnsureSuccessStatusCode();

                var getBody = await getResponse.Content.ReadAsStringAsync();

                var items = JsonConvert.DeserializeObject<List<WipBySerialResponseItem>>(getBody)
                            ?? new List<WipBySerialResponseItem>();

                var pairs = items.SelectMany(rootItem =>
                {
                    var list = new List<WipSerial>();

                    if (rootItem.WipId.HasValue && rootItem.WipId.Value > 0)
                    {
                        var sn = !string.IsNullOrWhiteSpace(rootItem.SerialNumber)
                                    ? rootItem.SerialNumber
                                    : (rootItem.Panel?.PanelSerialNumber ?? string.Empty);

                        if (!string.IsNullOrWhiteSpace(sn))
                        {
                            list.Add(new WipSerial
                            {
                                WipId = rootItem.WipId.Value,
                                SerialNumber = sn
                            });
                        }
                    }

                    if (rootItem.Panel?.PanelWips != null)
                    {
                        foreach (var pw in rootItem.Panel.PanelWips)
                        {
                            if (pw?.WipId.HasValue == true && pw.WipId.Value > 0)
                            {
                                if (!string.IsNullOrWhiteSpace(pw.SerialNumber))
                                {
                                    list.Add(new WipSerial
                                    {
                                        WipId = pw.WipId.Value,
                                        SerialNumber = pw.SerialNumber
                                    });
                                }
                            }
                        }
                    }

                    return list;
                })
                .GroupBy(x => x.WipId)
                .Select(g => g.First())
                .OrderBy(x => x.WipId)
                .ToList();

                _wipIdsBySerial[serialNumber] = pairs
                    .Select(p => p.WipId)
                    .ToList();

                return pairs;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao pegar WipIds: {ex.Message}");
            }
        }
        public async Task OkToStartRework(int wipId, string resourceName, string serialNumber)
        {
            try
            {
                var url = $"{MesBaseUrl}api-external-api/api/PanelWip/startWip";

                var payload = new
                {
                    wipId = wipId,
                    serialNumber = serialNumber,
                    resourceName = resourceName
                };

                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();


                return;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao Ok To Start para Rework: {ex.Message}");
            }
        }
        public async Task AddRework(int wipId, int indicmentId)
        {
            try
            {
                var addReworkUrl = $"{MesBaseUrl}api-external-api/api/InspectionAndRework/{wipId}/addrework";

                var payload = new AddReworkRequest
                {
                    WipId = wipId,
                    ReworkCategory = "Rework",
                    Detail = "",
                    Comment = "string",
                    IndictmentId = indicmentId,
                    ReplaceDetail = new ReplaceDetail
                    {
                        SerialNumber = "string",
                        Grn = "string",
                        Upds = new List<UpdItem>(),
                        DataCollectionItems = new List<DataCollectionItem>()
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await _client.PostAsync(addReworkUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    await AbourtStarted(wipId);
                }

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao Adicionar Rework: {ex.Message}");
            }
        }
        public async Task CompleteRework(int wipId)
        {
            try
            {
                var url = $"{MesBaseUrl}/api-external-api/api/Wips/{wipId}/complete";

                var payload = new
                {
                    wipId = wipId
                };

                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();


                return;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao Ok To Start para Rework: {ex.Message}");
            }
        }

        public async Task AbourtStarted(int wipId)
        {
            try
            {
                var url = $"{MesBaseUrl}/api-external-api/api/Wips/{wipId}/abort";

                var payload = new
                {
                    wipId = wipId,
                    isSingleWipMode = true
                };

                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();


                return;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao Ok To Start para Rework: {ex.Message}");
            }

        }

        public async Task<OperationInfo?> GetOperationInfoAsync(string serialNumber)
        {
            var url = $"{MesBaseUrl}api-external-api/api/Wips/OperationHistories?SiteName=MANAUS&SerialNumber={serialNumber}";
            var resp = await _client.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync();

            var root = JObject.Parse(body);

            var wips = (JArray?)root["Wips"];
            var wip = wips?
                .FirstOrDefault(w => string.Equals((string?)w["SerialNumber"], serialNumber, StringComparison.OrdinalIgnoreCase))
                ?? wips?.FirstOrDefault();

            if (wip == null)
                return null;

            var opHistories = (JArray?)wip["OperationHistories"];
            var op = opHistories?.FirstOrDefault();
            if (op == null)
                return new OperationInfo
                {
                    SerialNumber = (string?)wip["SerialNumber"],
                    WipId = (int?)wip["WipId"],
                    CustomerName = (string?)wip["CustomerName"]
                };

            // Monta o objeto único
            var result = new OperationInfo
            {
                // Do Wip
                SerialNumber = (string?)wip["SerialNumber"],
                WipId = (int?)wip["WipId"],
                CustomerName = (string?)wip["CustomerName"],

                // Do OperationHistory 
                ManufacturingArea = (string?)op["ManufacturingArea"],
                RouteStepId = (int?)op["RouteStepId"],
                RouteStepName = (string?)op["RouteStepName"],
                Resource = (string?)op["Resource"]
            };

            return result;
        }


        public async Task<List<SPIWipInfo>> GetPanelWipInfoAsync(string runBarCode, CancellationToken ct = default)
        {
            var url = $"{MesBaseUrl}api-external-api/api/Wips/GetWipIdBySerialNumber?SiteName=MANAUS&SerialNumber={Uri.EscapeDataString(runBarCode)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return new List<SPIWipInfo>();

            JToken token;
            try
            {
                token = JToken.Parse(body);
            }
            catch (Exception ex)
            {

                throw;
            }

            List<SPIWipInfo> result = new();

            if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    var info = MapSpiWipInfo(item as JObject);
                    if (info != null)
                    {
                        // Dedup e normalização de PanelWips
                        NormalizePanel(info);
                        result.Add(info);
                    }
                }
            }
            else if (token is JObject obj)
            {
                // Caso raro: a API retornar um único objeto
                var info = MapSpiWipInfo(obj);
                if (info != null)
                {
                    NormalizePanel(info);
                    result.Add(info);
                }
            }

            return result;
        }



        #region PALLET TRACKING

        // 1. Endpoint que adiciona um "Atributo" no Jemsmm pelo WipId do produto pra cada wipid da board 

        public async Task AddAttribute(SPIInputModel input)
        {
            var serialNumber = input.Inspection.Barcode;
            var pallet = input.Pallet;

            var wipsIds = await GetWipIds(serialNumber);

            foreach (var wipId in wipsIds)
            {
                try
                {
                    var url = $"{MesBaseUrl}/api-external-api/api/Wips/{wipId.WipId}/attributes";

                    var payload = new AddAttributeDto
                    {
                        AttributeName = "Pallet",
                        AttributeType = "string",
                        AttributeValue = pallet,
                        WipId = wipId.WipId,
                        PanelAttributeList = new List<PanelAttributeList>
                        {
                            new PanelAttributeList
                            {
                                WipId = wipId.WipId,
                                AttributeAssignments = new List<AttributeAssignments>
                                {
                                    new AttributeAssignments
                                    {
                                        AttributeName = "Pallet",
                                        AttributeType = "string",
                                        AttributeValue = pallet
                                    }
                                }
                            }
                        }
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(payload);

                    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                    var response = await _client.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Erro ao processar wipId {wipId.WipId}: {ex.Message}");
                    continue;
                }
            }

            return;
        }
        #endregion






        #region SEARCH FERT IN BOM
        // get bom id 
        public async Task<int> GetAssemblyId(int wipId)
        {
            try
            {
                var wipURL = $"{MesBaseUrl}api-external-api/api/Wips/{wipId}/Bom";

                var response = await _client.GetAsync(wipURL);
                response.EnsureSuccessStatusCode();

                var getBody = await response.Content.ReadAsStringAsync();

                var json = JObject.Parse(getBody);

                var bomId = json["Wips"]?[0]?["Bom"]?["BomId"]?.Value<int>() ?? 0;

                return bomId;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao pegar AssemblyId: {ex.Message}");
            }
        }

        // bom structure
        public async Task<string> GetProgramInBom(int assemblyId)
        {
            var bomStructureUrl = $"{MesBaseUrl}api-external-api/api/boms/{assemblyId}/bomStructure";

            var response = await _client.GetAsync(bomStructureUrl);
            response.EnsureSuccessStatusCode();

            var getBody = await response.Content.ReadAsStringAsync();

            var json = JObject.Parse(getBody);

            var parentBomName = json["BomHierarchy"]?
                .FirstOrDefault(item => item["ParentBomName"]?.ToString().EndsWith("TOP") == true)?
                ["ParentBomName"]?.ToString();

            return parentBomName ?? string.Empty;
        }


        public async Task<string> GetProgramInBomSPI(int assemblyId)
        {
            var bomStructureUrl = $"{MesBaseUrl}api-external-api/api/boms/{assemblyId}/bomStructure";

            var response = await _client.GetAsync(bomStructureUrl);
            response.EnsureSuccessStatusCode();

            var getBody = await response.Content.ReadAsStringAsync();

            var json = JObject.Parse(getBody);

            var parentBomName = json["BomHierarchy"]?
                .FirstOrDefault()?["ParentBomName"]?.ToString();

            return parentBomName ?? string.Empty;
        }

        #endregion






        #region FullWipOperation
        public async Task<CompleteWipResponseModel> FullWipOpCompletePass(OperationInfo operationInfo, GetWipIdBySerialNumberResponseModels getWipResponse)
        {
            try
            {
                var url = $"{MesBaseUrl}/api-external-api/api/Wips/FullPerformWipOperations";

                var payload = new FullPerformWipOperationsRequest
                {
                    SiteName = "MANAUS",
                    CustomerName = operationInfo.CustomerName,
                    SerialNumber = operationInfo.SerialNumber,
                    MaterialName = getWipResponse.MaterialName,
                    ResourceName = operationInfo.Resource,
                    StartDateTime = "",
                    EndDateTime = "",
                    IsSingleWipMode = false,
                };

                var json = JsonConvert.SerializeObject(payload);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var completeWipResponse = JsonConvert.DeserializeObject<CompleteWipResponseModel>(responseContent);

                return completeWipResponse;
            }
            catch (Exception ex)
            {
                throw new FullWipOperationPassException($"Erro ao Full Wip Complete: Step não configurado corretamente para esse produto");
            }
        }
        #endregion



        public IReadOnlyList<int> GetCachedWipIds(string serialNumber)
        {
            return _wipIdsBySerial.TryGetValue(serialNumber, out var ids) ? ids : Array.Empty<int>();
        }

        public IReadOnlyList<int> GetCachedIndictmentIds(int wipId)
        {
            return _indictmentsByWip.TryGetValue(wipId, out var ids) ? ids : Array.Empty<int>();
        }


        public static async Task<string> SafeReadAsStringAsync(HttpContent content, CancellationToken ct)
        {
            try { return content == null ? string.Empty : await content.ReadAsStringAsync(ct); }
            catch { return string.Empty; }
        }


        public static SPIWipInfo MapSpiWipInfo(JObject obj)
        {
            if (obj == null) return null;

            var info = new SPIWipInfo
            {
                WipId = obj.Value<long?>("WipId") ?? 0,
                SerialNumber = obj.Value<string>("SerialNumber"),
                CustomerName = obj.Value<string>("CustomerName"),
                MaterialName = obj.Value<string>("MaterialName"),
                IsAssembled = obj.Value<bool?>("IsAssembled") ?? false
            };

            var panelObj = obj["Panel"] as JObject;
            if (panelObj != null)
            {
                var panel = new Domain.V1.DTOs.InputModels.PanelInfo
                {
                    PanelId = panelObj.Value<long?>("PanelId") ?? 0,
                    PanelSerialNumber = panelObj.Value<string>("PanelSerialNumber"),
                    ConfiguredWipPerPanel = panelObj.Value<string>("ConfiguredWipPerPanel"),
                    ActualWipPerPanel = panelObj.Value<string>("ActualWipPerPanel"),
                    PanelWips = new List<PanelWip>()
                };

                var panelWipsArr = panelObj["PanelWips"] as JArray;
                if (panelWipsArr != null)
                {
                    foreach (var pwTok in panelWipsArr.OfType<JObject>())
                    {
                        var pw = new PanelWip
                        {
                            WipId = pwTok.Value<long?>("WipId") ?? 0,
                            SerialNumber = pwTok.Value<string>("SerialNumber"),
                            PanelPosition = pwTok.Value<int?>("PanelPosition") ?? 0,
                            IsPanelBroken = pwTok.Value<bool?>("IsPanelBroken") ?? false
                        };
                        panel.PanelWips.Add(pw);
                    }
                }

                info.Panel = panel;
            }

            return info;
        }

        // Normaliza e deduplica PanelWips por posição e ordena
        public static void NormalizePanel(SPIWipInfo info)
        {
            if (info == null) return;
            info.Panel ??= new Domain.V1.DTOs.InputModels.PanelInfo();
            info.Panel.PanelWips ??= new List<PanelWip>();

            // Dedup por PanelPosition, preferindo não quebrados e com SerialNumber preenchido
            info.Panel.PanelWips = info.Panel.PanelWips
                .Where(pw => pw.PanelPosition > 0) // filtro básico
                .GroupBy(pw => pw.PanelPosition)
                .Select(g => g
                    .OrderBy(pw => pw.IsPanelBroken) // false (não quebrado) primeiro
                    .ThenBy(pw => string.IsNullOrWhiteSpace(pw.SerialNumber)) // preferir que tenha serial
                    .First())
                .OrderBy(pw => pw.PanelPosition)
                .ToList();
        }
    }
}