using Microsoft.AspNetCore.Mvc;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Infrastructure.Configuration;
using System.Diagnostics;

namespace MovieReviewApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;
        private readonly InstanceManager _instanceManager;
        private readonly GladiaService _gladiaService;
        private readonly ILogger<HealthController> _logger;

        public HealthController(
            MongoDbService mongoDbService,
            InstanceManager instanceManager,
            GladiaService gladiaService,
            ILogger<HealthController> logger)
        {
            _mongoDbService = mongoDbService;
            _instanceManager = instanceManager;
            _gladiaService = gladiaService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            HealthCheckResult result = await PerformHealthChecks();
            
            if (result.IsHealthy)
            {
                return Ok(result);
            }
            else
            {
                return StatusCode(503, result); // Service Unavailable
            }
        }

        [HttpGet("database")]
        public async Task<IActionResult> CheckDatabase()
        {
            try
            {
                bool isConnected = await _mongoDbService.TestConnectionAsync();
                
                if (isConnected)
                {
                    return Ok(new { status = "healthy", message = "Database connection successful" });
                }
                else
                {
                    return StatusCode(503, new { status = "unhealthy", message = "Database connection failed" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return StatusCode(503, new { status = "unhealthy", message = $"Database error: {ex.Message}" });
            }
        }

        [HttpGet("audio")]
        public async Task<IActionResult> CheckAudioProcessing()
        {
            try
            {
                // Check if FFmpeg is available for audio conversion
                bool ffmpegAvailable = await CheckFFmpegAsync();
                
                return Ok(new { 
                    status = ffmpegAvailable ? "healthy" : "degraded", 
                    message = ffmpegAvailable ? "Audio processing available" : "FFmpeg not found - audio conversion unavailable",
                    ffmpeg = ffmpegAvailable
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio processing health check failed");
                return StatusCode(503, new { status = "unhealthy", message = $"Audio processing error: {ex.Message}" });
            }
        }

        [HttpGet("instance/{instanceName}")]
        public IActionResult CheckInstanceHealth(string instanceName)
        {
            try
            {
                InstanceManager instanceManager = new InstanceManager(instanceName);
                InstanceConfig config = instanceManager.GetInstanceConfig();
                
                return Ok(new { 
                    status = "healthy", 
                    instance = instanceName,
                    displayName = config.DisplayName,
                    environment = config.Environment,
                    port = config.Port,
                    lastUsed = config.LastUsed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Instance health check failed for {InstanceName}", instanceName);
                return StatusCode(503, new { status = "unhealthy", message = $"Instance error: {ex.Message}" });
            }
        }

        private async Task<HealthCheckResult> PerformHealthChecks()
        {
            HealthCheckResult result = new HealthCheckResult();
            
            try
            {
                // Database connectivity
                result.Database = await _mongoDbService.TestConnectionAsync();
                
                // Audio processing capabilities
                result.AudioProcessing = await CheckFFmpegAsync();
                
                // Instance configuration
                try
                {
                    InstanceConfig config = _instanceManager.GetInstanceConfig();
                    result.InstanceConfig = config != null;
                    result.InstanceName = _instanceManager.InstanceName;
                    result.Port = config?.Port ?? 0;
                }
                catch
                {
                    result.InstanceConfig = false;
                }
                
                result.IsHealthy = result.Database && result.InstanceConfig;
                result.Status = result.IsHealthy ? "healthy" : "unhealthy";
                result.Timestamp = DateTime.UtcNow;
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return new HealthCheckResult
                {
                    IsHealthy = false,
                    Status = "unhealthy",
                    Database = false,
                    AudioProcessing = false,
                    InstanceConfig = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        private async Task<bool> CheckFFmpegAsync()
        {
            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process process = new Process { StartInfo = processInfo };
                process.Start();
                await process.WaitForExitAsync();
                
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public class HealthCheckResult
    {
        public bool IsHealthy { get; set; }
        public string Status { get; set; } = "unknown";
        public bool Database { get; set; }
        public bool AudioProcessing { get; set; }
        public bool InstanceConfig { get; set; }
        public string? InstanceName { get; set; }
        public int Port { get; set; }
        public string? Error { get; set; }
        public DateTime Timestamp { get; set; }
    }
}