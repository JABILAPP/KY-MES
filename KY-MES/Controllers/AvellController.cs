using KY_MES.Application.Exceptions;
using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace KY_MES.Controllers
{
    [Route("avell")]
    [ApiController]
    public class AvellController : ControllerBase
    {
        private readonly IKY_MESApplication _application;

        public AvellController(IKY_MESApplication application)
        {
            _application = application;
        }


        [HttpPost("notebook")]
        public async Task<IActionResult> NotebookSPISendWipData([FromBody] SPIInputModel sPIInput)
        {
            try
            {
                //var response = await _application.SPISendWipDataLog(sPIInput);
                var response = await _application.NotebookSendWipData(sPIInput);

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
