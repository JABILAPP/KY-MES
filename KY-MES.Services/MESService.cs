using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Services.DomainServices.Interfaces;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace KY_MES.Services
{
    public class MESService : IMESService
    {
        public static string MesBaseUrl = Environment.GetEnvironmentVariable("MES_BASE_URL");
        private readonly CookieContainer _cookieContainer;
        private readonly HttpClientHandler _handler;
        private readonly HttpClient _client;


        private readonly ConcurrentDictionary<int, List<int>> _indictmentsByWip = new ConcurrentDictionary<int, List<int>>();
        private readonly ConcurrentDictionary<string, List<int>> _wipIdsBySerial = new ConcurrentDictionary<string, List<int>>();




        public MESService()
        {
            // Creating a CookieContainer to store the cookie
            _cookieContainer = new CookieContainer();

            // Initializing HttpClientHandler with the CookieContainer
            _handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer
            };

            // Initializing HttpClient with the handler
            _client = new HttpClient(_handler);
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
                throw new Exception($"Erro ao tentar o login com as credenciais fornecidas. Mensagem: {ex.Message}");
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
            catch (Exception ex) { throw new Exception($"Erro ao coletar o WipId. Mensagem: {ex.Message}"); }

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

                throw new Exception($"Erro ao fazer o Check PV. Mensagem: {ex.Message}");
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
                throw new Exception($"Erro ao executar StartWip. Mensagem: {ex.Message}");
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
                throw new Exception($"Erro ao executar CompleteWipFail. Mensagem: {ex.Message}");
            }
        }

        public async Task<AddDefectResponseModel> AddDefectAsync(AddDefectRequestModel addDefectRequestModel, int WipId)
        {
            try
            {
                // Remove duplicatas dentro de cada panelDefect
                if (addDefectRequestModel.panelDefects != null)
                {
                    foreach (var panel in addDefectRequestModel.panelDefects)
                    {
                        if (panel.defects != null)
                        {
                            panel.defects = panel.defects
                                .GroupBy(d => new { d.defectName, d.defectCRD }) // define critério de unicidade
                                .Select(g => g.First())
                                .ToList();
                        }
                    }
                }

                var addDefectUrl = $"{MesBaseUrl}api-external-api/api/Wips/{WipId}/AddDefects";

                var jsonContent = JsonConvert.SerializeObject(addDefectRequestModel);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _client.PostAsync(addDefectUrl, content);
                response.EnsureSuccessStatusCode();

                await CompleteWipIoTAsync(WipId);

                return new AddDefectResponseModel();
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao executar AddDefect. Mensagem: {ex.Message}");
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
                                // Usa o SerialNumber do próprio PanelWip, que é o desejado
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
                // Remove duplicados por WipId, mantendo o primeiro par encontrado
                .GroupBy(x => x.WipId)
                .Select(g => g.First())
                .OrderBy(x => x.WipId)
                .ToList();

                // Se você ainda quiser manter um cache, mude o tipo do dicionário:
                // Dictionary<string, List<WipSerial>> _wipIdsBySerial;
                _wipIdsBySerial[serialNumber] = pairs
                    .Select(p => p.WipId) // se o cache antigo precisa só de IDs, mantenha assim
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





        public IReadOnlyList<int> GetCachedWipIds(string serialNumber)
        {
            return _wipIdsBySerial.TryGetValue(serialNumber, out var ids) ? ids : Array.Empty<int>();
        }

        public IReadOnlyList<int> GetCachedIndictmentIds (int wipId)
        {
            return _indictmentsByWip.TryGetValue(wipId, out var ids) ? ids : Array.Empty<int>();
        }
    
    
    }
}