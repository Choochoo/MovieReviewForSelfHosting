using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing sound clips and their lifecycle.
/// Handles CRUD operations, file management, and sound clip state.
/// </summary>
public class SoundClipService(
    MongoDbService databaseService,
    ILogger<SoundClipService> logger,
    IWebHostEnvironment webHostEnvironment)
    : BaseService<SoundClip>(databaseService, logger)
{
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

    // Base CRUD methods are inherited from BaseService<SoundClip>
    // GetAllAsync, GetByIdAsync(Guid), CreateAsync, UpdateAsync, DeleteAsync(Guid)


    public async Task<List<SoundClip>> GetByPersonIdAsync(string personId)
    {
        IEnumerable<SoundClip> soundClips = await _db.GetAllAsync<SoundClip>();
        return soundClips
            .Where(s => s.PersonId == personId && s.IsActive)
            .OrderBy(s => s.CreatedAt)
            .ToList();
    }

    public async Task<SoundClip> SaveAsync(string personId, IFormFile file, string? description = null)
    {
        string uploadsPath = GetUploadsPath();
        _ = Directory.CreateDirectory(uploadsPath);

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

        return await CreateAsync(soundClip);
    }

    public async Task<SoundClip> SaveFromUrlAsync(string personId, string url, string? description = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException("Invalid URL format", nameof(url));

        using HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MovieReviewApp/1.0");

        HttpResponseMessage response = await httpClient.GetAsync(url);
        _ = response.EnsureSuccessStatusCode();

        string uploadsPath = GetUploadsPath();
        _ = Directory.CreateDirectory(uploadsPath);

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

        return await CreateAsync(soundClip);
    }

    public async Task<Dictionary<string, int>> GetCountsByPersonAsync()
    {
        IEnumerable<SoundClip> soundClips = await _db.GetAllAsync<SoundClip>();
        return soundClips
            .Where(s => s.IsActive)
            .GroupBy(s => s.PersonId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public string GetSoundClipUrl(SoundClip soundClip)
    {
        return $"/sounds/{soundClip.FileName}";
    }

    // Wrapper methods for backward compatibility with SoundController
    public async Task<SoundClip> SaveSoundClipAsync(string personId, IFormFile file, string? description = null)
    {
        return await SaveAsync(personId, file, description);
    }

    public async Task<SoundClip> SaveSoundClipFromUrlAsync(string personId, string url, string? description = null)
    {
        return await SaveFromUrlAsync(personId, url, description);
    }

    public async Task<Dictionary<string, int>> GetSoundClipCountsByPersonAsync()
    {
        return await GetCountsByPersonAsync();
    }

    public async Task<List<SoundClip>> GetSoundClipsForPersonAsync(string personId)
    {
        return await GetByPersonIdAsync(personId);
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
            return cleanName;
        }
        catch
        {
            return "audio";
        }
    }
}
