using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.FileSystem;

public class AudioFileOrganizer
{
    private readonly ILogger<AudioFileOrganizer> _logger;
    private readonly IWebHostEnvironment _environment;

    public AudioFileOrganizer(ILogger<AudioFileOrganizer> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Simply ensures the session folder exists - no complex status folder structure
    /// </summary>
    public void InitializeAudioFolders(string sessionFolderPath)
    {
        Directory.CreateDirectory(sessionFolderPath);
        _logger.LogInformation("Session folder created/verified: {SessionPath}", sessionFolderPath);
    }

    /// <summary>
    /// Gets all audio files in the session folder
    /// </summary>
    public List<string> GetAudioFilesInSession(string sessionFolderPath)
    {
        if (!Directory.Exists(sessionFolderPath))
        {
            return new List<string>();
        }

        HashSet<string> audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma",
            ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp"
        };

        return Directory.GetFiles(sessionFolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f)))
            .ToList();
    }

    /// <summary>
    /// Helper to check if a file is locked
    /// </summary>
    public bool IsFileLocked(string filePath)
    {
        try
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Close();
            }
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}