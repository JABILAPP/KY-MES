using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.Interfaces;
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

                // var response = await _application.SPISendWipDataLog(sPIInput);
                var response = await _application.SPISendWipData(sPIInput);

                return Ok(new
                {
                    Result = "OK",
                    Success = true,
                    Code = 200
                });


                // return Ok(response);

            }
            catch (Exception ex)
            {
                return BadRequest("Error while sending SPI data to MES: " + ex.Message);
            }
        }
    }
}
