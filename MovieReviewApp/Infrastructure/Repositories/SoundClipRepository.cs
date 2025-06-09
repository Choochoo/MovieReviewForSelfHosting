using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class SoundClipRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<SoundClipRepository> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public SoundClipRepository(
            IDatabaseService databaseService,
            ILogger<SoundClipRepository> logger,
            IWebHostEnvironment webHostEnvironment)
        {
            _databaseService = databaseService;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<List<SoundClip>> GetAllAsync()
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

        public async Task<SoundClip?> GetByIdAsync(string id)
        {
            try
            {
                return await _databaseService.GetByIdAsync<SoundClip>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sound clip by id {Id}", id);
                return null;
            }
        }

        public async Task<List<SoundClip>> GetByPersonIdAsync(string personId)
        {
            try
            {
                IEnumerable<SoundClip> soundClips = await _databaseService.GetAllAsync<SoundClip>();
                return soundClips
                    .Where(s => s.PersonId == personId && s.IsActive)
                    .OrderBy(s => s.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sound clips for person {PersonId}", personId);
                return new List<SoundClip>();
            }
        }

        public async Task<SoundClip> SaveAsync(string personId, IFormFile file, string? description = null)
        {
            try
            {
                string uploadsPath = GetUploadsPath();
                Directory.CreateDirectory(uploadsPath);

                string fileName = $"{Guid.NewGuid()}_{file.FileName}";
                string filePath = Path.Combine(uploadsPath, fileName);

                using (FileStream stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                SoundClip soundClip = new SoundClip
                {
                    PersonId = personId,
                    FileName = fileName,
                    OriginalFileName = file.FileName,
                    FilePath = filePath,
                    ContentType = file.ContentType,
                    FileSize = file.Length,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                };

                await _databaseService.InsertAsync(soundClip);
                _logger.LogInformation("Saved sound clip {FileName} for person {PersonId}", fileName, personId);
                
                return soundClip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sound clip for person {PersonId}", personId);
                throw;
            }
        }

        public async Task<SoundClip> SaveFromUrlAsync(string personId, string url, string? description = null)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                    throw new ArgumentException("Invalid URL format", nameof(url));

                using HttpClient httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "MovieReviewApp/1.0");
                
                HttpResponseMessage response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string uploadsPath = GetUploadsPath();
                Directory.CreateDirectory(uploadsPath);

                string? contentType = response.Content.Headers.ContentType?.MediaType;
                
                if (!string.IsNullOrEmpty(contentType) && contentType.Contains("text/html"))
                    throw new InvalidOperationException("The URL points to an HTML page, not an audio file. Please use a direct link to an audio file.");
                
                if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("audio/"))
                {
                    string urlExtension = Path.GetExtension(uri.LocalPath).ToLower();
                    contentType = urlExtension switch
                    {
                        ".mp3" => "audio/mpeg",
                        ".wav" => "audio/wav",
                        ".ogg" => "audio/ogg",
                        ".m4a" => "audio/mp4",
                        ".aac" => "audio/aac",
                        _ => throw new InvalidOperationException("URL does not appear to point to a supported audio file. Supported formats: MP3, WAV, OGG, M4A, AAC")
                    };
                }

                string extension = GetExtensionFromContentType(contentType);
                string fileName = $"{Guid.NewGuid()}{extension}";
                string filePath = Path.Combine(uploadsPath, fileName);

                byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                
                if (fileBytes.Length < 1024)
                    throw new InvalidOperationException("Downloaded file is too small to be valid audio");

                await File.WriteAllBytesAsync(filePath, fileBytes);

                string originalFileName = GetCleanFileName(uri);

                SoundClip soundClip = new SoundClip
                {
                    PersonId = personId,
                    FileName = fileName,
                    OriginalFileName = originalFileName,
                    FilePath = filePath,
                    ContentType = contentType,
                    FileSize = fileBytes.Length,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                };

                await _databaseService.InsertAsync(soundClip);
                _logger.LogInformation("Saved sound clip from URL for person {PersonId}: {OriginalFileName}", personId, originalFileName);
                
                return soundClip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sound clip from URL {Url} for person {PersonId}", url, personId);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                SoundClip? soundClip = await GetByIdAsync(id);
                if (soundClip == null)
                    return false;

                soundClip.IsActive = false;
                soundClip.UpdatedAt = DateTime.UtcNow;
                await _databaseService.UpsertAsync(soundClip);

                if (File.Exists(soundClip.FilePath))
                    File.Delete(soundClip.FilePath);

                _logger.LogInformation("Deleted sound clip {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete sound clip {Id}", id);
                return false;
            }
        }

        public async Task<Dictionary<string, int>> GetCountsByPersonAsync()
        {
            try
            {
                IEnumerable<SoundClip> soundClips = await _databaseService.GetAllAsync<SoundClip>();
                return soundClips
                    .Where(s => s.IsActive)
                    .GroupBy(s => s.PersonId)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sound clip counts by person");
                return new Dictionary<string, int>();
            }
        }

        public string GetSoundClipUrl(SoundClip soundClip)
        {
            return $"/sounds/{soundClip.FileName}";
        }

        private string GetUploadsPath()
        {
            return Path.Combine(_webHostEnvironment.WebRootPath, "sounds");
        }

        private static string GetExtensionFromContentType(string contentType)
        {
            return contentType.ToLower() switch
            {
                "audio/mpeg" => ".mp3",
                "audio/wav" => ".wav",
                "audio/ogg" => ".ogg",
                "audio/aac" => ".aac",
                "audio/mp4" => ".m4a",
                _ => ".mp3"
            };
        }

        private static string GetCleanFileName(Uri uri)
        {
            try
            {
                string fileName = Path.GetFileName(uri.LocalPath);
                
                if (string.IsNullOrEmpty(fileName))
                    fileName = uri.Segments.LastOrDefault()?.Trim('/') ?? "audio";
                
                string cleanName = fileName.Split('?')[0].Split('#')[0];
                
                if (string.IsNullOrEmpty(cleanName) || cleanName.StartsWith('.'))
                    cleanName = $"audio_{DateTime.Now:yyyyMMdd_HHmmss}";
                
                return cleanName;
            }
            catch
            {
                return $"audio_{DateTime.Now:yyyyMMdd_HHmmss}";
            }
        }
    }
} 