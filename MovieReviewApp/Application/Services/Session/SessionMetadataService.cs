using MovieReviewApp.Models;
using NAudio.Wave;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Application.Services.Session;

/// <summary>
/// Service responsible for extracting and processing metadata from movie session folders and audio files.
/// Handles folder scanning, date/title extraction, and audio file analysis.
/// </summary>
public class SessionMetadataService
{
    private readonly ILogger<SessionMetadataService> _logger;
    private readonly IWebHostEnvironment _environment;

    public SessionMetadataService(ILogger<SessionMetadataService> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
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

        // Determine participants using mic assignments if available
        DetermineParticipants(session);

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

    /// <summary>
    /// Scans a session folder for audio files and populates the session's AudioFiles collection.
    /// </summary>
    public async Task ScanAudioFilesAsync(MovieSession session)
    {
        if (string.IsNullOrEmpty(session.FolderPath) || !Directory.Exists(session.FolderPath))
        {
            _logger.LogWarning("Session folder path is invalid or doesn't exist: {FolderPath}", session.FolderPath);
            return;
        }

        string[] supportedExtensions = { ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac", ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp" };
        List<string> audioFiles = new List<string>();

        foreach (string extension in supportedExtensions)
        {
            string[] files = Directory.GetFiles(session.FolderPath, $"*{extension}", SearchOption.TopDirectoryOnly);
            audioFiles.AddRange(files);
        }

        _logger.LogInformation("Found {FileCount} audio files in {FolderPath}", audioFiles.Count, session.FolderPath);

        session.AudioFiles.Clear();
        Dictionary<string, string> identifiedFiles = new Dictionary<string, string>();

        foreach (string filePath in audioFiles.OrderBy(f => f))
        {
            string fileName = Path.GetFileName(filePath);
            string fileNameUpper = fileName.ToUpper();

            AudioFile audioFile = new AudioFile
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = new FileInfo(filePath).Length,
                ProcessingStatus = AudioProcessingStatus.Pending,
                CurrentStep = "Waiting to upload",
                ProgressPercentage = 0,
                LastUpdated = DateTime.UtcNow,
                CanRetry = true
            };

            // Get file duration if possible
            audioFile.Duration = await GetMediaDurationAsync(filePath);

            // Analyze file name patterns to determine type and speaker
            AnalyzeAudioFileName(audioFile, identifiedFiles);

            session.AudioFiles.Add(audioFile);
        }

        // Handle master recording identification
        IdentifyMasterRecording(session, identifiedFiles);
    }

    /// <summary>
    /// Analyzes an audio file name to determine speaker information and file type.
    /// </summary>
    private void AnalyzeAudioFileName(AudioFile audioFile, Dictionary<string, string> identifiedFiles)
    {
        string fileName = audioFile.FileName;
        string fileNameUpper = fileName.ToUpper();

        // Check for MIC1-9 pattern (convert 1-based file naming to 0-based speaker numbers)
        Match micMatch = Regex.Match(fileNameUpper, @"^MIC(\d)\.WAV$");
        if (micMatch.Success)
        {
            int micFileNumber = int.Parse(micMatch.Groups[1].Value); // 1-based from file
            audioFile.SpeakerNumber = micFileNumber - 1; // Convert to 0-based for consistency
            identifiedFiles[fileName] = $"MIC{micFileNumber}";
            _logger.LogDebug("Identified microphone file: {FileName} as MIC{MicNumber} (Speaker {SpeakerNumber})", 
                fileName, micFileNumber, audioFile.SpeakerNumber);
        }
        // Check for existing speaker pattern (convert 1-based file naming to 0-based speaker numbers)
        else if (Regex.IsMatch(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase))
        {
            Match match = Regex.Match(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase);
            int speakerFileNumber = int.Parse(match.Groups[1].Value); // 1-based from file
            audioFile.SpeakerNumber = speakerFileNumber - 1; // Convert to 0-based for consistency
            identifiedFiles[fileName] = $"Speaker{speakerFileNumber}";
        }
        // Skip PHONE.WAV and SOUND_PAD.WAV files
        else if (fileNameUpper == "PHONE.WAV" || fileNameUpper == "SOUND_PAD.WAV" || fileNameUpper == "SOUNDPAD.WAV")
        {
            identifiedFiles[fileName] = fileNameUpper.Contains("PHONE") ? "Phone" : "SoundPad";
            _logger.LogDebug("Identified {FileType} file: {FileName}", 
                fileNameUpper.Contains("PHONE") ? "phone" : "sound pad", fileName);
        }
        // Check for master recording with timestamp pattern (e.g., 2024_1122_1839.wav)
        else if (Regex.IsMatch(fileName, @"^\d{4}_\d{4}_\d{4}\.(wav|mp3|m4a|aac|ogg|flac)$", RegexOptions.IgnoreCase))
        {
            audioFile.IsMasterRecording = true;
            identifiedFiles[fileName] = "TimestampedMaster";
            _logger.LogDebug("Identified timestamped master mix file: {FileName}", fileName);
        }
        // Check for master recording patterns
        else if (fileName.ToLower().Contains("master") ||
                 fileName.ToLower().Contains("combined") ||
                 fileName.ToLower().Contains("full") ||
                 fileName.ToLower().Contains("group"))
        {
            audioFile.IsMasterRecording = true;
            identifiedFiles[fileName] = "Master";
        }
    }

    /// <summary>
    /// Identifies the master recording file if not already detected.
    /// </summary>
    private void IdentifyMasterRecording(MovieSession session, Dictionary<string, string> identifiedFiles)
    {
        // Any unidentified file is likely the master mix (critical file)
        List<AudioFile> unidentifiedFiles = session.AudioFiles.Where(f => !identifiedFiles.ContainsKey(f.FileName)).ToList();
        
        if (unidentifiedFiles.Count == 1)
        {
            unidentifiedFiles[0].IsMasterRecording = true;
            _logger.LogDebug("Identified master mix file by elimination: {FileName}", unidentifiedFiles[0].FileName);
        }
        else if (unidentifiedFiles.Count > 1)
        {
            // If multiple unidentified files, pick the largest as master mix
            AudioFile largestFile = unidentifiedFiles.OrderByDescending(f => f.FileSize).First();
            largestFile.IsMasterRecording = true;
            _logger.LogDebug("Multiple unidentified files. Selected {FileName} as master mix based on size", largestFile.FileName);
        }

        // Rename master recording to MASTER_MIX with original extension preserved
        AudioFile? masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording);
        if (masterFile != null && !masterFile.FileName.StartsWith("MASTER_MIX", StringComparison.OrdinalIgnoreCase))
        {
            RenameMasterRecording(masterFile);
        }

        // Log warning if no master recording found
        if (!session.AudioFiles.Any(f => f.IsMasterRecording))
        {
            _logger.LogWarning("CRITICAL: No master mix file identified in session");
        }
    }

    /// <summary>
    /// Renames the master recording file to follow naming conventions.
    /// </summary>
    private void RenameMasterRecording(AudioFile masterFile)
    {
        string oldPath = masterFile.FilePath;
        string? directory = Path.GetDirectoryName(oldPath);
        string originalExtension = Path.GetExtension(oldPath);
        string newFileName = $"MASTER_MIX{originalExtension}";
        string newPath = Path.Combine(directory!, newFileName);

        try
        {
            // If MASTER_MIX file already exists with this extension, don't overwrite
            if (!File.Exists(newPath))
            {
                File.Move(oldPath, newPath);
                masterFile.FileName = newFileName;
                masterFile.FilePath = newPath;
                _logger.LogDebug("Renamed master mix file from {OldName} to {NewName}", 
                    Path.GetFileName(oldPath), newFileName);
            }
            else
            {
                _logger.LogDebug("{NewFileName} already exists, keeping original file name: {FileName}", 
                    newFileName, masterFile.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename master mix file from {FileName} to {NewFileName}", 
                masterFile.FileName, newFileName);
        }
    }

    /// <summary>
    /// Determines participants based on audio files and speaker assignments.
    /// </summary>
    public void DetermineParticipants(MovieSession session)
    {
        // Based on audio files, determine who was present (using zero-based speaker numbers)
        List<int> presentSpeakers = session.AudioFiles
            .Where(f => f.SpeakerNumber.HasValue)
            .Select(f => f.SpeakerNumber!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // All possible speakers (0-8) - supports up to 9 microphones (zero-based)
        List<int> allSpeakers = Enumerable.Range(0, 9).ToList();
        List<int> absentSpeakers = allSpeakers.Except(presentSpeakers).ToList();

        // Use mic assignments if available, otherwise show generic mic labels
        session.ParticipantsPresent = presentSpeakers.Select(mic =>
            session.MicAssignments.TryGetValue(mic, out string? name) && !string.IsNullOrEmpty(name)
                ? name
                : $"Mic {mic + 1}" // Convert to 1-based for display
        ).ToList();

        session.ParticipantsAbsent = absentSpeakers.Select(mic =>
            session.MicAssignments.TryGetValue(mic, out string? name) && !string.IsNullOrEmpty(name)
                ? name
                : $"Mic {mic + 1}" // Convert to 1-based for display
        ).ToList();

        _logger.LogInformation("Determined participants - Present: {PresentCount}, Absent: {AbsentCount}", 
            session.ParticipantsPresent.Count, session.ParticipantsAbsent.Count);
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