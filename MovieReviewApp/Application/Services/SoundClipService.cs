using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using System.Security.Cryptography;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing sound clips and their lifecycle.
/// Handles CRUD operations, blob storage, and sound clip state.
/// </summary>
public class SoundClipService(
    MongoDbService databaseService,
    ILogger<SoundClipService> logger)
    : BaseService<SoundClipStorage>(databaseService, logger)
{

    // Base CRUD methods are inherited from BaseService<SoundClipStorage>
    // GetAllAsync, GetByIdAsync(Guid), CreateAsync, UpdateAsync, DeleteAsync(Guid)


    public async Task<List<SoundClipStorage>> GetByPersonIdAsync(string personId)
    {
        IEnumerable<SoundClipStorage> soundClips = await _db.GetAllAsync<SoundClipStorage>();
        return soundClips
            .Where(s => s.PersonId == personId && s.IsActive)
            .OrderBy(s => s.CreatedAt)
            .ToList();
    }

    public async Task<SoundClipStorage> SaveAsync(string personId, IFormFile file, string? description = null)
    {
        using MemoryStream memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        byte[] audioData = memoryStream.ToArray();

        // Check for duplicates using hash
        string hash = ComputeHash(audioData);
        SoundClipStorage? existingSoundClip = await GetByHashAsync(hash);
        if (existingSoundClip != null)
        {
            _logger.LogInformation("Sound clip with hash {Hash} already exists, returning existing", hash);
            return existingSoundClip;
        }

        string fileName = $"{Guid.NewGuid()}_{file.FileName}";

        SoundClipStorage soundClip = new SoundClipStorage
        {
            PersonId = personId,
            FileName = fileName,
            OriginalFileName = file.FileName,
            ContentType = file.ContentType,
            AudioData = audioData,
            FileSize = file.Length,
            Hash = hash,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        return await CreateAsync(soundClip);
    }

    public async Task<SoundClipStorage> SaveFromUrlAsync(string personId, string url, string? description = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException("Invalid URL format", nameof(url));

        using HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MovieReviewApp/1.0");

        HttpResponseMessage response = await httpClient.GetAsync(url);
        _ = response.EnsureSuccessStatusCode();

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

        byte[] audioData = await response.Content.ReadAsByteArrayAsync();

        if (audioData.Length < 1024)
            throw new InvalidOperationException("Downloaded file is too small to be valid audio");

        // Check for duplicates using hash
        string hash = ComputeHash(audioData);
        var existingSoundClip = await GetByHashAsync(hash);
        if (existingSoundClip != null)
        {
            _logger.LogInformation("Sound clip with hash {Hash} already exists, returning existing", hash);
            return existingSoundClip;
        }

        string extension = GetExtensionFromContentType(contentType);
        string fileName = $"{Guid.NewGuid()}{extension}";
        string originalFileName = GetCleanFileName(uri);

        SoundClipStorage soundClip = new SoundClipStorage
        {
            PersonId = personId,
            FileName = fileName,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            AudioData = audioData,
            FileSize = audioData.Length,
            Hash = hash,
            OriginalUrl = url,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        return await CreateAsync(soundClip);
    }

    public async Task<Dictionary<string, int>> GetCountsByPersonAsync()
    {
        IEnumerable<SoundClipStorage> soundClips = await _db.GetAllAsync<SoundClipStorage>();
        return soundClips
            .Where(s => s.IsActive)
            .GroupBy(s => s.PersonId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public string GetSoundClipUrl(SoundClipStorage soundClip)
    {
        return $"/api/sound/{soundClip.Id}";
    }

    public async Task<SoundClipStorage?> GetSoundClipAsync(Guid soundClipId)
    {
        return await GetByIdAsync(soundClipId);
    }

    // Wrapper methods for backward compatibility with SoundController
    public async Task<SoundClipStorage> SaveSoundClipAsync(string personId, IFormFile file, string? description = null)
    {
        return await SaveAsync(personId, file, description);
    }

    public async Task<SoundClipStorage> SaveSoundClipFromUrlAsync(string personId, string url, string? description = null)
    {
        return await SaveFromUrlAsync(personId, url, description);
    }

    public async Task<Dictionary<string, int>> GetSoundClipCountsByPersonAsync()
    {
        return await GetCountsByPersonAsync();
    }

    public async Task<List<SoundClipStorage>> GetSoundClipsForPersonAsync(string personId)
    {
        return await GetByPersonIdAsync(personId);
    }

    private async Task<SoundClipStorage?> GetByHashAsync(string hash)
    {
        var soundClips = await _db.GetAllAsync<SoundClipStorage>();
        return soundClips.FirstOrDefault(s => s.Hash == hash);
    }

    private static string ComputeHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);
        return Convert.ToBase64String(hash);
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
            return cleanName;
        }
        catch
        {
            return "audio";
        }
    }
}
