using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class SoundboardService
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<SoundboardService> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public SoundboardService(
            MongoDbService mongoDbService, 
            ILogger<SoundboardService> logger,
            IWebHostEnvironment webHostEnvironment)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<List<Person>> GetPeopleAsync()
        {
            try
            {
                var people = await _mongoDbService.GetAllAsync<Person>();
                return people.OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get people");
                return new List<Person>();
            }
        }

        public async Task<List<SoundClip>> GetSoundClipsForPersonAsync(string personId)
        {
            try
            {
                var soundClips = await _mongoDbService.GetAllAsync<SoundClip>();
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

        public async Task<SoundClip?> GetSoundClipByIdAsync(string id)
        {
            try
            {
                return await _mongoDbService.GetByIdAsync<SoundClip>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sound clip by id {Id}", id);
                return null;
            }
        }

        public async Task<SoundClip> SaveSoundClipAsync(string personId, IFormFile file, string? description = null)
        {
            try
            {
                var uploadsPath = GetUploadsPath();
                Directory.CreateDirectory(uploadsPath);

                var fileName = $"{Guid.NewGuid()}_{file.FileName}";
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
                    ContentType = file.ContentType,
                    FileSize = file.Length,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                };

                await _mongoDbService.InsertAsync(soundClip);
                _logger.LogInformation("Saved sound clip {FileName} for person {PersonId}", fileName, personId);
                
                return soundClip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sound clip for person {PersonId}", personId);
                throw;
            }
        }

        public async Task<SoundClip> SaveSoundClipFromUrlAsync(string personId, string url, string? description = null)
        {
            try
            {
                // Validate URL
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    throw new ArgumentException("Invalid URL format", nameof(url));
                }

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "MovieReviewApp/1.0");
                
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var uploadsPath = GetUploadsPath();
                Directory.CreateDirectory(uploadsPath);

                // Try to get content type from response
                var contentType = response.Content.Headers.ContentType?.MediaType;
                
                // Check if the response is actually HTML (not audio)
                if (!string.IsNullOrEmpty(contentType) && contentType.Contains("text/html"))
                {
                    throw new InvalidOperationException("The URL points to an HTML page, not an audio file. Please use a direct link to an audio file.");
                }
                
                // If no content type or not audio, try to detect from URL
                if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("audio/"))
                {
                    var urlExtension = Path.GetExtension(uri.LocalPath).ToLower();
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

                var extension = GetExtensionFromContentType(contentType);
                var fileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsPath, fileName);

                // Download and validate file size
                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                
                // Basic validation - check if it's not too small (likely an error page)
                if (fileBytes.Length < 1024) // Less than 1KB is suspicious for audio
                {
                    throw new InvalidOperationException("Downloaded file is too small to be valid audio");
                }

                await File.WriteAllBytesAsync(filePath, fileBytes);

                // Get a clean original filename from URL
                var originalFileName = GetCleanFileName(uri);

                var soundClip = new SoundClip
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

                await _mongoDbService.InsertAsync(soundClip);
                _logger.LogInformation("Saved sound clip from URL for person {PersonId}: {OriginalFileName}", personId, originalFileName);
                
                return soundClip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sound clip from URL {Url} for person {PersonId}", url, personId);
                throw;
            }
        }

        public async Task<bool> DeleteSoundClipAsync(string id)
        {
            try
            {
                var soundClip = await GetSoundClipByIdAsync(id);
                if (soundClip == null)
                    return false;

                soundClip.IsActive = false;
                soundClip.UpdatedAt = DateTime.UtcNow;
                await _mongoDbService.UpsertAsync(soundClip);

                if (File.Exists(soundClip.FilePath))
                {
                    File.Delete(soundClip.FilePath);
                }

                _logger.LogInformation("Deleted sound clip {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete sound clip {Id}", id);
                return false;
            }
        }

        public async Task<Dictionary<string, int>> GetSoundClipCountsByPersonAsync()
        {
            try
            {
                var soundClips = await _mongoDbService.GetAllAsync<SoundClip>();
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

        public async Task<List<SoundClip>> GetAllSoundClipsAsync()
        {
            try
            {
                return await _mongoDbService.GetAllAsync<SoundClip>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all sound clips");
                return new List<SoundClip>();
            }
        }

        public string GetSoundClipUrl(SoundClip soundClip)
        {
            // Use static file serving instead of API controller
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
                var fileName = Path.GetFileName(uri.LocalPath);
                
                // If no filename in path, use last segment
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = uri.Segments.LastOrDefault()?.Trim('/') ?? "audio";
                }
                
                // Remove query parameters and fragments
                var cleanName = fileName.Split('?')[0].Split('#')[0];
                
                // If still empty or just extension, create a meaningful name
                if (string.IsNullOrEmpty(cleanName) || cleanName.StartsWith('.'))
                {
                    cleanName = $"audio_{DateTime.Now:yyyyMMdd_HHmmss}";
                }
                
                return cleanName;
            }
            catch
            {
                return $"audio_{DateTime.Now:yyyyMMdd_HHmmss}";
            }
        }
    }
}