using KY_MES.Application;
using KY_MES.Application.Exceptions;
using KY_MES.Application.Utils;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.Interfaces;
using KY_MES.Services.Exceptions;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace KY_MES.Controllers
{
    [ApiController]
    [Route("api")]
    [EnableCors("AllowAllOrigins")]
    public class KY_MESController : ControllerBase
    {
        private readonly IKY_MESApplication _application;
        public KY_MESController(IKY_MESApplication application = null)
        {
            _application = application;
        }

        [HttpPost]
        public async Task<IActionResult> SPISendWipData([FromBody] SPIInputModel sPIInput)
        {
            try
            {

                //var response = await _application.SPISendWipDataLog(sPIInput);
                var response = await _application.SPISendWipData(sPIInput);

                return Ok(new
                {
                    Result = "OK",
                    Success = true,
                    Code = 200
                });

            }
            catch (CheckPVFailedException ex)
            {
                return BadRequest(new
                {
                    ErrorType = $"PCB não está na rota correta: {ex.Message}"
                });
            }catch (BomProgramFailException ex)
            {
                return BadRequest(new
                {
                    ErrorType = $"Programa diferente para esse produto {ex.Message}"
                });
            }
            catch (FertSpiException ex)
            {
                return BadRequest(new
                {
                    ErrorType = $"FERT não encontrado no banco de dados: {ex.Message}"
                });
            }
            catch (SizeException ex)
            {
                return BadRequest(new
                {
                    ErrorType = $"Tamanho da Memoria GB não é compativel com o FERT: {ex.Message}"
                });
            }
            catch (FullWipOperationPassException ex)
            {
                return BadRequest(new
                {
                    ErrorType = $"Erro ao Full Wip Complete: Step não configurado corretamente para esse produto"
                });
            }
            catch (StartWipException ex)
            {
                return BadRequest(new
                {
                    ErrorType = $"Erro ao Iniciar o STEP na Maquina, verificar a rota do produto: {ex.Message}"
                });
            }
            catch (CompleteWipException ex)
            {
                return BadRequest(new
                {
                    ErrorType = $"Erro ao finalizar o registrar o Resultado no MES: {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    Result = "Error",
                    Success = false,
                    Code = 400,
                    Message = "Error while sending SPI data to MES: " + ex.Message
                });
            }
        }

    }
}
