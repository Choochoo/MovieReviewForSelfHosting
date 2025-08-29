using Microsoft.AspNetCore.Mvc;

namespace MovieReviewApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("ok");
        }
    }
}