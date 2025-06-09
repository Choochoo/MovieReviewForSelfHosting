using Microsoft.AspNetCore.Mvc;
using MovieReviewApp.Infrastructure.Repositories;

namespace MovieReviewApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SoundController : ControllerBase
    {
        private readonly SoundboardRepository _soundboardRepository;
        private readonly ILogger<SoundController> _logger;

        public SoundController(SoundboardRepository soundboardRepository, ILogger<SoundController> logger)
        {
            _soundboardRepository = soundboardRepository;
            _logger = logger;
        }

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
                Models.SoundClip soundClip = await _soundboardRepository.SaveSoundClipAsync(personId, file, description);
                return Ok(new { 
                    id = soundClip.Id.ToString(),
                    fileName = soundClip.FileName,
                    originalFileName = soundClip.OriginalFileName,
                    url = _soundboardRepository.GetSoundClipUrl(soundClip)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload sound for person {PersonId}", personId);
                return StatusCode(500, "Failed to upload sound");
            }
        }

        [HttpPost("upload-url")]
        public async Task<IActionResult> UploadSoundFromUrl([FromBody] UploadUrlRequest request)
        {
            if (string.IsNullOrEmpty(request.PersonId) || string.IsNullOrEmpty(request.Url))
            {
                return BadRequest("PersonId and URL are required");
            }

            try
            {
                Models.SoundClip soundClip = await _soundboardRepository.SaveSoundClipFromUrlAsync(request.PersonId, request.Url, request.Description);
                return Ok(new { 
                    id = soundClip.Id.ToString(),
                    fileName = soundClip.FileName,
                    originalFileName = soundClip.OriginalFileName,
                    url = _soundboardRepository.GetSoundClipUrl(soundClip)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload sound from URL for person {PersonId}", request.PersonId);
                return StatusCode(500, "Failed to upload sound from URL");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSound(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Sound ID is required");
            }

            try
            {
                bool success = await _soundboardRepository.DeleteSoundClipAsync(id);
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

        [HttpGet("/api/sound/serve/{fileName}")]
        public async Task<IActionResult> GetSound(string fileName)
        {
            try
            {
                _logger.LogInformation("Serving sound file via API: {FileName}", fileName);
                
                Models.SoundClip? soundClip = (await _soundboardRepository.GetAllSoundClipsAsync()).FirstOrDefault(s => s.FileName == fileName);
                if (soundClip == null || !soundClip.IsActive)
                {
                    _logger.LogWarning("Sound clip not found or inactive: {FileName}", fileName);
                    return NotFound();
                }

                if (!System.IO.File.Exists(soundClip.FilePath))
                {
                    _logger.LogWarning("Sound file does not exist on disk: {FilePath}", soundClip.FilePath);
                    return NotFound();
                }

                byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(soundClip.FilePath);
                _logger.LogInformation("Successfully serving sound file: {FileName}, Size: {Size} bytes", fileName, fileBytes.Length);
                
                return File(fileBytes, soundClip.ContentType, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to serve sound file {FileName}", fileName);
                return StatusCode(500);
            }
        }

        [HttpGet("/api/sound/test")]
        public IActionResult TestSoundEndpoint()
        {
            return Ok(new { message = "Sound controller is working", timestamp = DateTime.UtcNow });
        }

        [HttpGet("/api/sound/debug")]
        public async Task<IActionResult> DebugSounds()
        {
            try
            {
                List<Models.SoundClip> allSounds = await _soundboardRepository.GetAllSoundClipsAsync().ConfigureAwait(false);
                var debugInfo = allSounds.Where(s => s.IsActive).Select(s => new
                {
                    id = s.Id,
                    fileName = s.FileName,
                    originalFileName = s.OriginalFileName,
                    filePath = s.FilePath,
                    fileExists = System.IO.File.Exists(s.FilePath),
                    fileSize = s.FileSize,
                    contentType = s.ContentType,
                    url = _soundboardRepository.GetSoundClipUrl(s),
                    personId = s.PersonId
                }).ToList();

                return Ok(new { 
                    message = "Sound debug info", 
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