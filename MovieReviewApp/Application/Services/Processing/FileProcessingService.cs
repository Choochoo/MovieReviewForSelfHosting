using System.Text.RegularExpressions;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;
using NAudio.Wave;

namespace MovieReviewApp.Application.Services.Processing;

/// <summary>
/// Consolidated service for all file processing operations.
/// Handles audio file processing, transcription, and organization.
/// </summary>
public class FileProcessingService
{
    private readonly GladiaService _gladiaService;
    private readonly AudioProcessingStateMachine _stateMachine;
    private readonly MongoDbService _database;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileProcessingService> _logger;

    public FileProcessingService(
        GladiaService gladiaService,
        AudioProcessingStateMachine stateMachine,
        MongoDbService database,
        IWebHostEnvironment environment,
        ILogger<FileProcessingService> logger)
    {
        _gladiaService = gladiaService;
        _stateMachine = stateMachine;
        _database = database;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Processes a single audio file through the complete pipeline.
    /// </summary>
    public async Task ProcessAudioFileAsync(AudioFile file, Action<string, int>? progressCallback = null)
    {
        try
        {
            _logger.LogInformation("Starting processing for file {FileName}", file.FileName);
            file.ProcessingStatus = AudioProcessingStatus.ConvertingToMp3;
            file.CurrentStep = "Starting processing";
            file.ProgressPercentage = 0;

            // Step 1: Convert to MP3 if needed
            if (!file.FilePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                progressCallback?.Invoke("Converting to MP3", 10);
                file.CurrentStep = "Converting to MP3";
                file.ProgressPercentage = 10;
                await ConvertToMp3Async(file);
            }

            // Step 2: Transcribe audio
            progressCallback?.Invoke("Transcribing audio", 30);
            file.CurrentStep = "Transcribing audio";
            file.ProgressPercentage = 30;
            await TranscribeAudioAsync(file);

            // Step 3: Process transcript
            progressCallback?.Invoke("Processing transcript", 60);
            file.CurrentStep = "Processing transcript";
            file.ProgressPercentage = 60;
            await ProcessTranscriptAsync(file);

            // Step 4: Mark as complete
            file.ProcessingStatus = AudioProcessingStatus.TranscriptsDownloaded;
            file.CurrentStep = "Complete";
            file.ProgressPercentage = 100;
            file.CanRetry = false;

            _logger.LogInformation("Completed processing for file {FileName}", file.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file {FileName}", file.FileName);
            file.ProcessingStatus = AudioProcessingStatus.Failed;
            file.ConversionError = ex.Message;
            file.CurrentStep = "Processing failed";
            file.CanRetry = true;
            throw;
        }
    }

    /// <summary>
    /// Processes all audio files in a session.
    /// </summary>
    public async Task ProcessSessionFilesAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        _logger.LogInformation("Starting file processing for session {SessionId}", session.Id);

        foreach (AudioFile file in session.AudioFiles)
        {
            await ProcessAudioFileAsync(file, progressCallback);
        }

        _logger.LogInformation("Completed file processing for session {SessionId}", session.Id);
    }


    /// <summary>
    /// Converts an audio file to MP3 format.
    /// </summary>
    private async Task ConvertToMp3Async(AudioFile file)
    {
        try
        {
            string mp3Path = Path.ChangeExtension(file.FilePath, ".mp3");
            _ = await _gladiaService.ConvertToMp3Async(file.FilePath, mp3Path);
            file.FilePath = mp3Path;
            file.FileName = Path.GetFileName(mp3Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert file {FileName} to MP3", file.FileName);
            throw;
        }
    }

    /// <summary>
    /// Transcribes an audio file using Gladia.
    /// </summary>
    private async Task TranscribeAudioAsync(AudioFile file)
    {
        try
        {
            _logger.LogInformation("Starting transcription for file {FileName}", file.FileName);

            // Upload file to Gladia if not already uploaded
            if (string.IsNullOrEmpty(file.AudioUrl))
            {
                file.AudioUrl = await _gladiaService.UploadFileAsync(file.FilePath, file);
                file.UploadedAt = DateTime.UtcNow;
                _logger.LogInformation("Uploaded {FileName} to Gladia with URL: {AudioUrl}", 
                    file.FileName, file.AudioUrl);
            }

            // Start transcription with default speaker count of 2 (can be overridden for specific files)
            int speakerCount = DetermineExpectedSpeakers(file.FileName);
            string transcriptionId = await _gladiaService.StartTranscriptionAsync(
                file.AudioUrl, 
                speakerCount, 
                enableSpeakerDiarization: true, 
                file.FileName);
            
            file.TranscriptId = transcriptionId;
            _logger.LogInformation("Started transcription {TranscriptionId} for {FileName}", 
                transcriptionId, file.FileName);

            // Wait for transcription completion
            TranscriptionResult result = await _gladiaService.WaitForTranscriptionAsync(transcriptionId);
            
            // Extract the basic transcript text
            file.TranscriptText = result.result?.transcription?.full_transcript ?? string.Empty;
            file.ProcessedAt = DateTime.UtcNow;

            _logger.LogInformation("Successfully transcribed {FileName}, transcript length: {Length} characters", 
                file.FileName, file.TranscriptText.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe file {FileName}", file.FileName);
            throw;
        }
    }

    /// <summary>
    /// Determines the expected number of speakers based on filename patterns.
    /// Individual mic files have 1 speaker, master/mix files typically have multiple.
    /// </summary>
    private static int DetermineExpectedSpeakers(string fileName)
    {
        string upperFileName = fileName.ToUpperInvariant();
        
        // Individual mic files typically have 1 speaker
        if (System.Text.RegularExpressions.Regex.IsMatch(upperFileName, @"^MIC\d+\.(WAV|MP3)$") ||
            upperFileName.StartsWith("PHONE.") ||
            upperFileName.StartsWith("SOUND_PAD.") ||
            upperFileName.StartsWith("SOUNDPAD.") ||
            upperFileName.StartsWith("USB."))
        {
            return 1;
        }
        
        // Master/mix files typically have multiple speakers
        if (upperFileName.Contains("MIX") || upperFileName.Contains("MASTER"))
        {
            return 4; // Default for group discussions
        }
        
        return 2; // Default fallback
    }

    /// <summary>
    /// Processes the transcript text to extract additional information.
    /// </summary>
    private async Task ProcessTranscriptAsync(AudioFile file)
    {
        try
        {
            // Add any additional transcript processing here
            // For example: speaker diarization, sentiment analysis, etc.
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process transcript for file {FileName}", file.FileName);
            throw;
        }
    }

    /// <summary>
    /// Prepares a movie session from a folder containing audio files, extracting metadata and scanning for audio content.
    /// </summary>
    public async Task<MovieSession> PrepareSessionFromFolderAsync(string folderPath, Dictionary<int, string>? micAssignments = null)
    {
        string folderName = Path.GetFileName(folderPath);

        // Extract date from folder name (format: YYYY-MonthName-MovieTitle)
        DateTime sessionDate = ExtractDateFromFolderName(folderName);

        // Extract movie title from folder name
        string movieTitle = SuggestMovieTitle(folderName);

        MovieSession session = new MovieSession
        {
            Date = sessionDate,
            MovieTitle = movieTitle,
            FolderPath = folderPath,
            Status = ProcessingStatus.Pending,
            MicAssignments = micAssignments ?? new Dictionary<int, string>()
        };

        // Scan for audio files
        await ScanAudioFilesAsync(session);

        _logger.LogInformation("Prepared movie session for {MovieTitle} on {Date} with {FileCount} audio files",
            session.MovieTitle, session.Date.ToShortDateString(), session.AudioFiles.Count);

        return session;
    }

    /// <summary>
    /// Extracts the session date from a folder name using various patterns.
    /// </summary>
    public DateTime ExtractDateFromFolderName(string folderName)
    {
        // Try format: YYYY-MonthName-MovieTitle
        Match monthNameMatch = Regex.Match(folderName, @"(\d{4})-([A-Za-z]+)-(.+)");
        if (monthNameMatch.Success)
        {
            try
            {
                int year = int.Parse(monthNameMatch.Groups[1].Value);
                string monthName = monthNameMatch.Groups[2].Value;

                // Parse month name to number
                if (DateTime.TryParseExact($"{monthName} 1, {year}", "MMMM d, yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime sessionDate))
                {
                    _logger.LogInformation("Extracted date {Date} from folder name", sessionDate.ToShortDateString());
                    return sessionDate;
                }
                else
                {
                    _logger.LogWarning("Invalid month name '{MonthName}' in folder name, using current date", monthName);
                }
            }
            catch
            {
                _logger.LogWarning("Invalid date format in folder name, using current date");
            }
        }

        // Try format: YYYY-MM-DD
        Match isoDateMatch = Regex.Match(folderName, @"(\d{4})-(\d{2})-(\d{2})");
        if (isoDateMatch.Success)
        {
            try
            {
                int year = int.Parse(isoDateMatch.Groups[1].Value);
                int month = int.Parse(isoDateMatch.Groups[2].Value);
                int day = int.Parse(isoDateMatch.Groups[3].Value);
                DateTime sessionDate = new DateTime(year, month, day);
                _logger.LogInformation("Extracted ISO date {Date} from folder name", sessionDate.ToShortDateString());
                return sessionDate;
            }
            catch
            {
                _logger.LogWarning("Invalid ISO date format in folder name, using current date");
            }
        }

        _logger.LogDebug("No date found in folder name, using current date");
        return DateTime.Now;
    }

    /// <summary>
    /// Suggests a movie title from the folder name by cleaning and formatting.
    /// </summary>
    public string SuggestMovieTitle(string folderName)
    {
        // Remove date patterns
        string title = Regex.Replace(folderName, @"^\d{4}-[A-Za-z]+-", "");
        title = Regex.Replace(title, @"^\d{4}-\d{2}-\d{2}-?", "");

        // Replace underscores and hyphens with spaces
        title = title.Replace("_", " ").Replace("-", " ");

        // Remove common file extensions
        title = Regex.Replace(title, @"\.(mp3|wav|m4a|aac|ogg|flac)$", "", RegexOptions.IgnoreCase);

        // Clean up extra spaces
        title = Regex.Replace(title, @"\s+", " ").Trim();

        // Capitalize first letter of each word
        title = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title.ToLower());

        return !string.IsNullOrEmpty(title) ? title : "Unknown Movie";
    }

    private async Task ScanAudioFilesAsync(MovieSession session)
    {
        string[] audioExtensions = { ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac" };
        string[] audioFiles = Directory.GetFiles(session.FolderPath)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLower()))
            .ToArray();

        foreach (string filePath in audioFiles)
        {
            string fileName = Path.GetFileName(filePath);
            AudioFile audioFile = new AudioFile
            {
                FileName = fileName,
                FilePath = filePath,
                ProcessingStatus = AudioProcessingStatus.Pending,
                CurrentStep = "Ready for processing",
                ProgressPercentage = 0,
                LastUpdated = DateTime.UtcNow
            };

            session.AudioFiles.Add(audioFile);
        }

        _logger.LogInformation("Scanned {FileCount} audio files in {FolderPath}",
            session.AudioFiles.Count, session.FolderPath);
    }

    /// <summary>
    /// Gets a standardized participant name for a given microphone number.
    /// </summary>
    public static string GetParticipantName(int micNumber)
    {
        return $"Mic {micNumber}";
    }

    /// <summary>
    /// Gets the duration of a media file using NAudio.
    /// </summary>
    private async Task<TimeSpan?> GetMediaDurationAsync(string filePath)
    {
        try
        {
            await Task.Yield(); // Make this async for consistency
            using MediaFoundationReader reader = new MediaFoundationReader(filePath);
            return reader.TotalTime;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get duration for file {FilePath}", filePath);
            return null;
        }
    }
}

/// <summary>
/// Represents a summary of cleanup operations performed on abandoned sessions.
/// </summary>
public class CleanupSummary
{
    public int ProcessedSessions { get; set; }
    public int RecoveredSessions { get; set; }
    public int FailedSessions { get; set; }
    public int ErrorSessions { get; set; }
}
