using MovieReviewApp.Models;

namespace MovieReviewApp.Services;

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
    /// Gets the appropriate folder path based on audio processing status
    /// Creates root-level processing directories with movie subdirectories
    /// Structure: root/{status}/{moviename}/
    /// </summary>
    public string GetFolderForStatus(AudioProcessingStatus status, string sessionFolderPath)
    {
        var statusFolder = GetStatusFolderName(status);

        // Extract movie name and ensure we get the root uploads directory
        string movieName, rootDirectory;
        
        // Check if sessionFolderPath already contains a status folder
        var parentDir = Path.GetDirectoryName(sessionFolderPath);
        var parentDirName = Path.GetFileName(parentDir)?.ToLowerInvariant();
        
        if (IsStatusFolder(parentDirName))
        {
            // sessionFolderPath is already inside a status folder (e.g., /uploads/failed/moviename/)
            // Extract the movie name and get the true root directory
            movieName = Path.GetFileName(sessionFolderPath);
            rootDirectory = Path.GetDirectoryName(parentDir); // Go up two levels to get root
        }
        else
        {
            // sessionFolderPath is at the correct level (e.g., /uploads/moviename/)
            movieName = Path.GetFileName(sessionFolderPath);
            rootDirectory = Path.GetDirectoryName(sessionFolderPath);
        }
        
        // Create structure: root/{status}/{moviename}/
        var folderPath = Path.Combine(rootDirectory!, statusFolder, movieName);
        EnsureFolderExists(folderPath);
        return folderPath;
    }

    /// <summary>
    /// Moves an audio file to the appropriate folder based on its processing status
    /// Handles cleanup of source directories after successful transitions
    /// </summary>
    public async Task<string> MoveFileToStatusFolder(AudioFile audioFile, string sessionFolderPath, bool cleanupSource = false)
    {
        try
        {
            // Validate that MP3 files don't go back to WAV folders
            if (!string.IsNullOrEmpty(audioFile.Mp3FilePath) && 
                (audioFile.ProcessingStatus == AudioProcessingStatus.Pending || 
                 audioFile.ProcessingStatus == AudioProcessingStatus.Failed))
            {
                _logger.LogError("Invalid state transition: MP3 file cannot go to {Status} folder", audioFile.ProcessingStatus);
                throw new InvalidOperationException($"MP3 files cannot be moved to {audioFile.ProcessingStatus} folders");
            }
            
            var targetFolder = GetFolderForStatus(audioFile.ProcessingStatus, sessionFolderPath);
            var targetFilePath = Path.Combine(targetFolder, Path.GetFileName(audioFile.FilePath));
            var sourceFolder = Path.GetDirectoryName(audioFile.FilePath);

            // If file is already in the correct location, no need to move
            if (string.Equals(audioFile.FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("File {FileName} already in correct status folder, skipping move", Path.GetFileName(audioFile.FilePath));
                return targetFilePath;
            }
            
            // Also check if file is already in any status subfolder for this status
            var currentFolder = Path.GetDirectoryName(audioFile.FilePath);
            var expectedStatusFolder = Path.GetFileName(targetFolder);
            if (currentFolder != null && Path.GetFileName(currentFolder) == expectedStatusFolder)
            {
                _logger.LogDebug("File {FileName} already in {StatusFolder} folder, skipping move", Path.GetFileName(audioFile.FilePath), expectedStatusFolder);
                return audioFile.FilePath;
            }

            // Ensure source file exists
            if (!File.Exists(audioFile.FilePath))
            {
                _logger.LogWarning("Source file not found for move operation: {SourcePath}", audioFile.FilePath);
                return audioFile.FilePath;
            }

            // If target file already exists, create a unique name
            if (File.Exists(targetFilePath))
            {
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFile.FilePath);
                var extension = Path.GetExtension(audioFile.FilePath);
                var counter = 1;
                
                do
                {
                    var newFileName = $"{fileNameWithoutExt}_{counter}{extension}";
                    targetFilePath = Path.Combine(targetFolder, newFileName);
                    counter++;
                } while (File.Exists(targetFilePath));
            }

            // Move the file
            File.Move(audioFile.FilePath, targetFilePath);
            
            _logger.LogInformation("Moved audio file {FileName} from {SourcePath} to {TargetPath} (Status: {Status})", 
                Path.GetFileName(audioFile.FilePath), 
                audioFile.FilePath,
                targetFilePath,
                audioFile.ProcessingStatus);

            // Cleanup source directory if requested
            if (cleanupSource && !string.IsNullOrEmpty(sourceFolder))
            {
                await CleanupEmptyStatusFolder(sourceFolder, sessionFolderPath);
                
                // Also check if we need to cleanup the root movie folder
                var rootMovieFolder = GetRootMovieFolder(sessionFolderPath);
                if (Directory.Exists(rootMovieFolder))
                {
                    var remainingFiles = Directory.GetFiles(rootMovieFolder, "*.*", SearchOption.AllDirectories);
                    if (remainingFiles.Length == 0)
                    {
                        Directory.Delete(rootMovieFolder, true);
                        _logger.LogInformation("Cleaned up empty root movie folder: {FolderPath}", rootMovieFolder);
                    }
                }
            }

            return targetFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move audio file {FilePath} to status folder {Status}", 
                audioFile.FilePath, audioFile.ProcessingStatus);
            return audioFile.FilePath; // Return original path if move fails
        }
    }

    /// <summary>
    /// Moves MP3 file to the appropriate folder based on processing status
    /// </summary>
    public async Task<string> MoveMp3FileToStatusFolder(AudioFile audioFile, string sessionFolderPath, bool cleanupSource = false)
    {
        if (string.IsNullOrEmpty(audioFile.Mp3FilePath) || !File.Exists(audioFile.Mp3FilePath))
        {
            return audioFile.Mp3FilePath ?? string.Empty;
        }

        try
        {
            var targetFolder = GetFolderForStatus(audioFile.ProcessingStatus, sessionFolderPath);
            var targetFilePath = Path.Combine(targetFolder, Path.GetFileName(audioFile.Mp3FilePath));
            var sourceFolder = Path.GetDirectoryName(audioFile.Mp3FilePath);

            // If file is already in the correct location, no need to move
            if (string.Equals(audioFile.Mp3FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return targetFilePath;
            }

            // If target file already exists, create a unique name
            if (File.Exists(targetFilePath))
            {
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFile.Mp3FilePath);
                var extension = Path.GetExtension(audioFile.Mp3FilePath);
                var counter = 1;
                
                do
                {
                    var newFileName = $"{fileNameWithoutExt}_{counter}{extension}";
                    targetFilePath = Path.Combine(targetFolder, newFileName);
                    counter++;
                } while (File.Exists(targetFilePath));
            }

            // Move the MP3 file
            File.Move(audioFile.Mp3FilePath, targetFilePath);
            
            _logger.LogInformation("Moved MP3 file {FileName} to {Status} folder", 
                Path.GetFileName(audioFile.Mp3FilePath), audioFile.ProcessingStatus);

            // Cleanup source directory if requested
            if (cleanupSource && !string.IsNullOrEmpty(sourceFolder))
            {
                await CleanupEmptyStatusFolder(sourceFolder, sessionFolderPath);
                
                // Also check if we need to cleanup the root movie folder
                var rootMovieFolder = GetRootMovieFolder(sessionFolderPath);
                if (Directory.Exists(rootMovieFolder))
                {
                    var remainingFiles = Directory.GetFiles(rootMovieFolder, "*.*", SearchOption.AllDirectories);
                    if (remainingFiles.Length == 0)
                    {
                        Directory.Delete(rootMovieFolder, true);
                        _logger.LogInformation("Cleaned up empty root movie folder: {FolderPath}", rootMovieFolder);
                    }
                }
            }

            return targetFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move MP3 file {FilePath} to status folder {Status}", 
                audioFile.Mp3FilePath, audioFile.ProcessingStatus);
            return audioFile.Mp3FilePath ?? string.Empty;
        }
    }

    /// <summary>
    /// Initializes the session folder (status folders created on-demand)
    /// </summary>
    public void InitializeAudioFolders(string sessionFolderPath)
    {
        // Status folders are now created on-demand when files are actually moved
        _logger.LogInformation("Audio processing folder structure will be created on-demand in session folder: {SessionPath}", sessionFolderPath);
    }

    /// <summary>
    /// Gets all files in a specific status folder
    /// </summary>
    public List<string> GetFilesInStatusFolder(AudioProcessingStatus status, string sessionFolderPath)
    {
        var folderPath = GetFolderForStatus(status, sessionFolderPath);
        
        if (!Directory.Exists(folderPath))
        {
            return new List<string>();
        }

        var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma",
            ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp"
        };

        return Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f)))
            .ToList();
    }

    /// <summary>
    /// Determines the processing status based on the file path
    /// Structure: root/{status}/{moviename}/file.ext
    /// </summary>
    public AudioProcessingStatus GetStatusFromPath(string filePath)
    {
        // Get the status folder (two levels up from file: file -> moviename -> status)
        var movieFolder = Path.GetDirectoryName(filePath);
        if (movieFolder == null) return AudioProcessingStatus.Pending;
        
        var statusFolder = Path.GetFileName(Path.GetDirectoryName(movieFolder))?.ToLowerInvariant();
        
        return statusFolder switch
        {
            "pending" => AudioProcessingStatus.Pending,
            "pending_mp3" => AudioProcessingStatus.PendingMp3,
            "failed_mp3" => AudioProcessingStatus.FailedMp3,
            "processed_mp3" => AudioProcessingStatus.ProcessedMp3,
            "failed" => AudioProcessingStatus.Failed,
            _ => AudioProcessingStatus.Pending
        };
    }

    /// <summary>
    /// Cleans up empty status folders after file movements
    /// Only removes movie folders within status directories that are completely empty
    /// Structure: root/{status}/{moviename}/
    /// </summary>
    private async Task CleanupEmptyStatusFolder(string folderPath, string sessionFolderPath)
    {
        try
        {
            var movieName = Path.GetFileName(sessionFolderPath);
            var rootDirectory = Path.GetDirectoryName(sessionFolderPath);
            
            // Check if this is a movie folder within a status directory
            var parentFolder = Path.GetFileName(Path.GetDirectoryName(folderPath))?.ToLowerInvariant();
            var isMovieFolderInStatusDir = IsStatusFolder(parentFolder);

            // Only cleanup movie folders within status directories
            if (isMovieFolderInStatusDir && Directory.Exists(folderPath))
            {
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    Directory.Delete(folderPath, true);
                    _logger.LogDebug("Cleaned up empty movie folder: {FolderPath}", folderPath);
                    
                    // Also cleanup the parent status folder if it's now empty
                    var statusFolder = Path.GetDirectoryName(folderPath);
                    if (Directory.Exists(statusFolder) && !Directory.GetFileSystemEntries(statusFolder).Any())
                    {
                        Directory.Delete(statusFolder);
                        _logger.LogDebug("Cleaned up empty status folder: {StatusFolder}", statusFolder);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup empty folder: {FolderPath}", folderPath);
        }
    }

    /// <summary>
    /// Validates that the directory structure follows the specification
    /// Ensures root-level processing directories with movie subdirectories
    /// Structure: root/{status}/{moviename}/
    /// </summary>
    public bool ValidateDirectoryStructure(string sessionFolderPath)
    {
        try
        {
            var statusFolders = GetAllStatusFolderNames();
            var violations = new List<string>();
            var rootDirectory = Path.GetDirectoryName(sessionFolderPath);
            var movieName = Path.GetFileName(sessionFolderPath);

            // Check for old nested structure that violates specification
            foreach (var statusFolder in statusFolders)
            {
                var oldNestedPath = Path.Combine(sessionFolderPath, statusFolder);
                if (Directory.Exists(oldNestedPath))
                {
                    violations.Add($"Invalid nested directory: {statusFolder}/ should be at root level as {statusFolder}/{movieName}/");
                }
            }

            // Validate correct structure exists at root level
            foreach (var statusFolder in statusFolders)
            {
                var correctPath = Path.Combine(rootDirectory!, statusFolder, movieName);
                if (Directory.Exists(correctPath))
                {
                    _logger.LogDebug("Valid structure found: {StatusPath}", correctPath);
                }
            }

            if (violations.Any())
            {
                _logger.LogError("Directory structure violations found for {MovieName}: {Violations}", 
                    movieName, string.Join(", ", violations));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate directory structure for {SessionPath}", sessionFolderPath);
            return false;
        }
    }

    /// <summary>
    /// Gets the root movie folder path, removing any status folder components
    /// Ensures we always work with the correct base path: root/{moviename}/
    /// </summary>
    public string GetRootMovieFolder(string currentPath)
    {
        // Get parent directory info
        var parentDir = Path.GetDirectoryName(currentPath);
        var parentDirName = Path.GetFileName(parentDir)?.ToLowerInvariant();
        
        if (IsStatusFolder(parentDirName))
        {
            // Current path is inside a status folder, extract movie name and root
            var movieName = Path.GetFileName(currentPath);
            var rootDirectory = Path.GetDirectoryName(parentDir);
            return Path.Combine(rootDirectory!, movieName);
        }
        
        // Current path is already at the correct level
        return currentPath;
    }

    /// <summary>
    /// Validates if a status transition is allowed based on workflow rules
    /// </summary>
    public bool IsValidStatusTransition(AudioProcessingStatus currentStatus, AudioProcessingStatus newStatus, bool isMP3)
    {
        // MP3 files can only be in MP3 folders
        if (isMP3 && (newStatus == AudioProcessingStatus.Pending || newStatus == AudioProcessingStatus.Failed))
        {
            return false;
        }

        // WAV files can only be in WAV folders
        if (!isMP3 && (newStatus == AudioProcessingStatus.PendingMp3 || 
                       newStatus == AudioProcessingStatus.FailedMp3 || 
                       newStatus == AudioProcessingStatus.ProcessedMp3))
        {
            return false;
        }

        // Valid transitions:
        // WAV: Pending -> Failed (conversion failure)
        // WAV: Pending -> PendingMp3 (converted to MP3)
        // MP3: PendingMp3 -> FailedMp3 (Gladia/OpenAI failure)
        // MP3: PendingMp3 -> ProcessedMp3 (complete success)
        // MP3: UploadedToGladia -> ProcessedMp3 (transcription complete)
        // MP3: UploadedToGladia -> FailedMp3 (transcription failure)
        
        return true;
    }

    /// <summary>
    /// Maps AudioProcessingStatus enum to folder names
    /// </summary>
    private string GetStatusFolderName(AudioProcessingStatus status)
    {
        return status switch
        {
            AudioProcessingStatus.Pending => "pending",
            AudioProcessingStatus.PendingMp3 => "pending_mp3",
            AudioProcessingStatus.FailedMp3 => "failed_mp3",
            AudioProcessingStatus.ProcessedMp3 => "processed_mp3",
            AudioProcessingStatus.UploadedToGladia => "pending_mp3", // Still pending transcription
            AudioProcessingStatus.TranscriptionComplete => "processed_mp3", // Completed files
            AudioProcessingStatus.Failed => "failed",
            _ => "pending"
        };
    }

    /// <summary>
    /// Checks if a folder name corresponds to a status folder
    /// </summary>
    private bool IsStatusFolder(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return false;
        
        // Get all possible folder names from the enum
        var allStatusFolders = Enum.GetValues<AudioProcessingStatus>()
            .Select(status => GetStatusFolderName(status))
            .Distinct();
        
        return allStatusFolders.Contains(folderName);
    }

    /// <summary>
    /// Gets all status folder names based on the AudioProcessingStatus enum
    /// </summary>
    private string[] GetAllStatusFolderNames()
    {
        // Get unique folder names from all enum values
        return Enum.GetValues<AudioProcessingStatus>()
            .Select(status => GetStatusFolderName(status))
            .Distinct()
            .ToArray();
    }

    private void EnsureFolderExists(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            _logger.LogDebug("Created audio processing folder: {FolderPath}", folderPath);
        }
    }
}