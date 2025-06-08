using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;
using System.Net.Http;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class SoundboardRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<SoundboardRepository> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IHttpClientFactory _httpClientFactory;

        public SoundboardRepository(
            IDatabaseService databaseService, 
            ILogger<SoundboardRepository> logger,
            IWebHostEnvironment webHostEnvironment,
            IHttpClientFactory httpClientFactory)
        {
            _databaseService = databaseService;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<List<Person>> GetPeopleAsync()
        {
            try
            {
                var people = await _databaseService.GetAllAsync<Person>();
                return people.OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get people");
                return new List<Person>();
            }
        }

        public async Task<SoundClip> SaveSoundClipAsync(string personId, IFormFile file, string? description = null)
        {
            try
            {
                var uploadsPath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "sounds");
                Directory.CreateDirectory(uploadsPath);

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                var filePath = Path.Combine(uploadsPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var soundClip = new SoundClip
                {
                    PersonId = personId,
                    FileName = fileName,
                    OriginalFileName = file.FileName,
                    FilePath = filePath,
                    ContentType = file.ContentType ?? "audio/mpeg",
                    FileSize = file.Length,
                    Description = description,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await _databaseService.InsertAsync(soundClip);
                return soundClip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sound clip");
                throw;
            }
        }

        public async Task<SoundClip> SaveSoundClipFromUrlAsync(string personId, string url, string? description = null)
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var uploadsPath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "sounds");
                Directory.CreateDirectory(uploadsPath);

                var uri = new Uri(url);
                var originalFileName = Path.GetFileName(uri.LocalPath) ?? "sound.mp3";
                var extension = Path.GetExtension(originalFileName);
                if (string.IsNullOrEmpty(extension))
                {
                    extension = ".mp3";
                    originalFileName += extension;
                }

                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsPath, fileName);

                await using var fileStream = new FileStream(filePath, FileMode.Create);
                await response.Content.CopyToAsync(fileStream);

                var fileInfo = new FileInfo(filePath);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";

                var soundClip = new SoundClip
                {
                    PersonId = personId,
                    FileName = fileName,
                    OriginalFileName = originalFileName,
                    FilePath = filePath,
                    ContentType = contentType,
                    FileSize = fileInfo.Length,
                    Description = description,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await _databaseService.InsertAsync(soundClip);
                return soundClip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sound clip from URL");
                throw;
            }
        }

        public string GetSoundClipUrl(SoundClip soundClip)
        {
            return $"/api/sound/serve/{soundClip.FileName}";
        }

        public async Task<bool> DeleteSoundClipAsync(string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var soundId))
                {
                    return false;
                }

                var soundClip = await _databaseService.GetByIdAsync<SoundClip>(soundId);
                if (soundClip == null)
                {
                    return false;
                }

                soundClip.IsActive = false;
                soundClip.UpdatedAt = DateTime.UtcNow;
                await _databaseService.UpsertAsync(soundClip);

                if (File.Exists(soundClip.FilePath))
                {
                    try
                    {
                        File.Delete(soundClip.FilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete physical file: {FilePath}", soundClip.FilePath);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete sound clip");
                return false;
            }
        }

        public async Task<List<SoundClip>> GetAllSoundClipsAsync()
        {
            try
            {
                return await _databaseService.GetAllAsync<SoundClip>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all sound clips");
                return new List<SoundClip>();
            }
        }

        public async Task<List<SoundClip>> GetSoundClipsForPersonAsync(string personId)
        {
            try
            {
                return await _databaseService.FindAsync<SoundClip>(s => s.PersonId == personId && s.IsActive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sound clips for person");
                return new List<SoundClip>();
            }
        }

        public async Task<Dictionary<string, int>> GetSoundClipCountsByPersonAsync()
        {
            try
            {
                var allClips = await _databaseService.FindAsync<SoundClip>(s => s.IsActive);
                return allClips
                    .GroupBy(s => s.PersonId)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sound clip counts");
                return new Dictionary<string, int>();
            }
        }
    }
}