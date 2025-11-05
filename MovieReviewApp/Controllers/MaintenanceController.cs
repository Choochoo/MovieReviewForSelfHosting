using Microsoft.AspNetCore.Mvc;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Infrastructure.Services;

namespace MovieReviewApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MaintenanceController : ControllerBase
    {
        private readonly DemoDataService _demoDataService;
        private readonly InstanceTypeService _instanceTypeService;
        private readonly ILogger<MaintenanceController> _logger;

        public MaintenanceController(
            DemoDataService demoDataService,
            InstanceTypeService instanceTypeService,
            ILogger<MaintenanceController> logger)
        {
            _demoDataService = demoDataService;
            _instanceTypeService = instanceTypeService;
            _logger = logger;
        }

        /// <summary>
        /// Backfills missing movie covers for all events
        /// Only available for demo instances
        /// </summary>
        [HttpPost("backfill-covers")]
        public async Task<IActionResult> BackfillMissingCovers()
        {
            try
            {
                // Only allow on demo instances
                if (!_instanceTypeService.ShouldGenerateDemoData())
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Cover backfill is only available for demo instances"
                    });
                }

                _logger.LogInformation("Starting manual cover backfill");

                var (successCount, failureCount) = await _demoDataService.BackfillMissingCoversAsync();

                _logger.LogInformation(
                    "Cover backfill completed: {Success} successful, {Failure} failed",
                    successCount,
                    failureCount
                );

                return Ok(new
                {
                    success = true,
                    message = "Cover backfill completed",
                    successCount,
                    failureCount,
                    totalProcessed = successCount + failureCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cover backfill");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred during cover backfill",
                    error = ex.Message
                });
            }
        }
    }
}
