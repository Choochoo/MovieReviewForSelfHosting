using Microsoft.AspNetCore.Components.Forms;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service for handling file uploads with progress tracking.
/// Eliminates code duplication across upload components.
/// </summary>
public class FileUploadService
{
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<FileUploadService> _logger;
    private const int ChunkSize = 1024 * 1024; // 1 MB chunks

    public FileUploadService(IWebHostEnvironment webHostEnvironment, ILogger<FileUploadService> logger)
    {
        _webHostEnvironment = webHostEnvironment;
        _logger = logger;
    }

    /// <summary>
    /// Uploads files to a session folder with progress tracking.
    /// </summary>
    public async Task<List<AudioFile>> UploadFilesWithProgressAsync(
        List<IBrowserFile> selectedFiles, 
        string sessionFolderPath,
        Dictionary<int, string> micAssignments,
        Action<string, int> progressCallback)
    {
        List<AudioFile> uploadedFiles = new();

        // Create the session folder if it doesn't exist
        Directory.CreateDirectory(sessionFolderPath);

        foreach (IBrowserFile browserFile in selectedFiles)
        {
            if (!AudioFileHelpers.IsAudioFile(browserFile.Name))
            {
                _logger.LogWarning("Skipping non-audio file: {FileName}", browserFile.Name);
                continue;
            }

            AudioFile audioFile = new AudioFile
            {
                FileName = browserFile.Name,
                FilePath = string.Empty,
                FileSize = browserFile.Size,
                ProcessingStatus = AudioProcessingStatus.Pending,
                CurrentStep = "Waiting to upload",
                ProgressPercentage = 0,
                CanRetry = true
            };

            uploadedFiles.Add(audioFile);

            // Update status to uploading
            audioFile.ProcessingStatus = AudioProcessingStatus.Uploading;
            audioFile.CurrentStep = "Uploading...";
            audioFile.ProgressPercentage = 0;

            string filePath = Path.Combine(sessionFolderPath, audioFile.FileName);

            try
            {
                // Upload file with progress tracking
                await using FileStream fs = new FileStream(filePath, FileMode.Create);
                using Stream browserStream = browserFile.OpenReadStream(maxAllowedSize: 10L * 1024 * 1024 * 1024);

                long totalBytes = browserFile.Size;
                long copiedBytes = 0;
                byte[] buffer = new byte[ChunkSize];
                int bytesRead;

                while ((bytesRead = await browserStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    copiedBytes += bytesRead;

                    int progressPercentage = (int)(copiedBytes * 100 / totalBytes);
                    if (progressPercentage != audioFile.ProgressPercentage)
                    {
                        audioFile.ProgressPercentage = progressPercentage;
                        audioFile.CurrentStep = $"Uploading... {AudioFileHelpers.FormatBytes(copiedBytes)} / {AudioFileHelpers.FormatBytes(totalBytes)}";
                        
                        // Call progress callback
                        progressCallback?.Invoke(audioFile.FileName, progressPercentage);
                    }
                }

                // Upload complete
                audioFile.FilePath = filePath;
                audioFile.ProcessingStatus = AudioProcessingStatus.Pending;
                audioFile.CurrentStep = "Ready for processing";
                audioFile.ProgressPercentage = 100;

                _logger.LogInformation("Successfully uploaded file: {FileName} to {FilePath}", audioFile.FileName, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file: {FileName}", audioFile.FileName);
                
                audioFile.ProcessingStatus = AudioProcessingStatus.Failed;
                audioFile.CurrentStep = "Upload failed";
                audioFile.ConversionError = ex.Message;
                audioFile.ProgressPercentage = 0;
                
                throw new InvalidOperationException($"Failed to upload {audioFile.FileName}: {ex.Message}", ex);
            }
        }

        return uploadedFiles;
    }

    /// <summary>
    /// Creates an AudioFile object from a browser file for preview purposes.
    /// </summary>
    public AudioFile CreateAudioFileFromBrowserFile(IBrowserFile browserFile, Dictionary<int, string> micAssignments)
    {
        return new AudioFile
        {
            FileName = browserFile.Name,
            FilePath = string.Empty,
            FileSize = browserFile.Size,
            ProcessingStatus = AudioProcessingStatus.Pending,
            CurrentStep = "Waiting to upload",
            ProgressPercentage = 0,
            CanRetry = true
        };
    }

    /// <summary>
    /// Validates upload prerequisites.
    /// </summary>
    public (bool IsValid, string ErrorMessage) ValidateUploadPrerequisites(
        string movieTitle, 
        HashSet<int> presentSpeakers, 
        List<IBrowserFile> selectedFiles)
    {
        if (string.IsNullOrWhiteSpace(movieTitle))
        {
            return (false, "Please select a movie first.");
        }

        if (!presentSpeakers.Any())
        {
            return (false, "Please select at least one participant.");
        }

        if (!selectedFiles.Any())
        {
            return (false, "Please select files to upload.");
        }

        // Check for valid audio files
        var audioFiles = selectedFiles.Where(f => AudioFileHelpers.IsAudioFile(f.Name)).ToList();
        if (!audioFiles.Any())
        {
            return (false, "No valid audio files selected.");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Creates a session folder path based on movie and date.
    /// </summary>
    public string CreateSessionFolderPath(string movieTitle, DateTime sessionDate, string baseDirectory = "uploads")
    {
        string folderName = AudioFileHelpers.GenerateFolderName(movieTitle, sessionDate);
        string baseUploadPath = Path.Combine(_webHostEnvironment.WebRootPath, baseDirectory);
        return Path.Combine(baseUploadPath, folderName);
    }

    /// <summary>
    /// Validates that all selected files are audio files.
    /// </summary>
    public List<string> GetInvalidFiles(List<IBrowserFile> selectedFiles)
    {
        return selectedFiles
            .Where(f => !AudioFileHelpers.IsAudioFile(f.Name))
            .Select(f => f.Name)
            .ToList();
    }
}