using Microsoft.AspNetCore.Mvc;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SoundController : ControllerBase
    {
        private readonly SoundClipService _soundClipService;
        private readonly ILogger<SoundController> _logger;

        /// <summary>
        /// Initializes a new instance of the SoundController class.
        /// </summary>
        /// <param name="soundClipService">The sound clip service.</param>
        /// <param name="logger">The logger for the controller.</param>
        public SoundController(SoundClipService soundClipService, ILogger<SoundController> logger)
        {
            _soundClipService = soundClipService;
            _logger = logger;
        }

        /// <summary>
        /// Uploads a sound file for a specific person.
        /// </summary>
        /// <param name="personId">The ID of the person associated with the sound.</param>
        /// <param name="file">The audio file to upload.</param>
        /// <param name="description">Optional description of the sound clip.</param>
        /// <returns>Details of the uploaded sound clip.</returns>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadSound([FromForm] string personId, [FromForm] IFormFile file, [FromForm] string? description = null)
        {
            if (string.IsNullOrEmpty(personId) || file == null || file.Length == 0)
            {
                return BadRequest("PersonId and file are required");
            }

            if (!IsAudioFile(file))
            {
                return BadRequest("Only audio files are allowed");
            }

            try
            {
                SoundClipStorage soundClip = await _soundClipService.SaveSoundClipAsync(personId, file, description);
                return Ok(new
                {
                    id = soundClip.Id.ToString(),
                    fileName = soundClip.FileName,
                    originalFileName = soundClip.OriginalFileName,
                    url = _soundClipService.GetSoundClipUrl(soundClip)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload sound for person {PersonId}", personId);
                return StatusCode(500, "Failed to upload sound");
            }
        }

        /// <summary>
        /// Uploads a sound file from a URL for a specific person.
        /// </summary>
        /// <param name="request">The request containing person ID, URL, and optional description.</param>
        /// <returns>Details of the uploaded sound clip.</returns>
        [HttpPost("upload-url")]
        public async Task<IActionResult> UploadSoundFromUrl([FromBody] UploadUrlRequest request)
        {
            if (string.IsNullOrEmpty(request.PersonId) || string.IsNullOrEmpty(request.Url))
            {
                return BadRequest("PersonId and URL are required");
            }

            try
            {
                SoundClipStorage soundClip = await _soundClipService.SaveSoundClipFromUrlAsync(request.PersonId, request.Url, request.Description);
                return Ok(new
                {
                    id = soundClip.Id.ToString(),
                    fileName = soundClip.FileName,
                    originalFileName = soundClip.OriginalFileName,
                    url = _soundClipService.GetSoundClipUrl(soundClip)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload sound from URL for person {PersonId}", request.PersonId);
                return StatusCode(500, "Failed to upload sound from URL");
            }
        }

        /// <summary>
        /// Deletes a sound clip by ID.
        /// </summary>
        /// <param name="id">The ID of the sound clip to delete.</param>
        /// <returns>OK if successful, NotFound if sound not found.</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSound(Guid id)
        {
            try
            {
                bool success = await _soundClipService.DeleteAsync(id);
                if (success)
                {
                    return Ok();
                }
                else
                {
                    return NotFound();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete sound {Id}", id);
                return StatusCode(500, "Failed to delete sound");
            }
        }

        private static bool IsAudioFile(IFormFile file)
        {
            string[] allowedTypes = new[]
            {
                "audio/mpeg",
                "audio/wav",
                "audio/ogg",
                "audio/aac",
                "audio/mp4",
                "audio/x-wav",
                "audio/wave"
            };

            return allowedTypes.Contains(file.ContentType?.ToLower());
        }

        /// <summary>
        /// Serves a sound file by ID.
        /// </summary>
        /// <param name="id">The ID of the sound clip to serve.</param>
        /// <returns>The sound file content with appropriate content type.</returns>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSound(Guid id)
        {
            try
            {
                _logger.LogInformation("Serving sound clip via API: {Id}", id);

                SoundClipStorage? soundClip = await _soundClipService.GetSoundClipAsync(id);
                if (soundClip == null || !soundClip.IsActive)
                {
                    _logger.LogWarning("Sound clip not found or inactive: {Id}", id);
                    return NotFound();
                }

                _logger.LogInformation("Successfully serving sound clip: {Id}, Size: {Size} bytes", id, soundClip.AudioData.Length);

                return File(soundClip.AudioData, soundClip.ContentType, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serve sound clip {Id}", id);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Tests the sound controller endpoint.
        /// </summary>
        /// <returns>Test response with current timestamp.</returns>
        [HttpGet("/api/sound/test")]
        public IActionResult TestSoundEndpoint()
        {
            return Ok(new { message = "Sound controller is working", timestamp = DateTime.UtcNow });
        }

        /// <summary>
        /// Provides debug information about all sound clips.
        /// </summary>
        /// <returns>Debug information including blob storage metadata.</returns>
        [HttpGet("/api/sound/debug")]
        public async Task<IActionResult> DebugSounds()
        {
            try
            {
                List<SoundClipStorage> allSounds = await _soundClipService.GetAllAsync();
                List<object> debugInfo = allSounds.Where(s => s.IsActive).Select(s => (object)new
                {
                    id = s.Id,
                    fileName = s.FileName,
                    originalFileName = s.OriginalFileName,
                    audioBlobSize = s.AudioData.Length,
                    fileSize = s.FileSize,
                    contentType = s.ContentType,
                    url = _soundClipService.GetSoundClipUrl(s),
                    personId = s.PersonId,
                    hash = s.Hash
                }).ToList();

                return Ok(new
                {
                    message = "Sound debug info (blob storage)",
                    timestamp = DateTime.UtcNow,
                    totalSounds = debugInfo.Count,
                    sounds = debugInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Debug endpoint failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public class UploadUrlRequest
        {
            public string PersonId { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string? Description { get; set; }
        }
    }
}
