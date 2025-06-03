using System.Text.RegularExpressions;
using MovieReviewApp.Models;
using MovieReviewApp.Database;
using System.Text.Json;
using NAudio.Wave;

namespace MovieReviewApp.Services;

public class MovieSessionService
{
    private readonly MongoDbService _database;
    private readonly GladiaService _gladiaService;
    private readonly MovieSessionAnalysisService _analysisService;
    private readonly AudioClipService _audioClipService;
    private readonly ILogger<MovieSessionService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    // Participant mapping for consistent MIC assignments
    private static readonly Dictionary<int, string> MicParticipantMap = new()
    {
        { 1, "Participant 1" }, // TODO: Update with actual names from configuration
        { 2, "Participant 2" },
        { 3, "Participant 3" },
        { 4, "Participant 4" },
        { 5, "Participant 5" },
        { 6, "Participant 6" }
    };

    public MovieSessionService(
        MongoDbService database,
        GladiaService gladiaService,
        MovieSessionAnalysisService analysisService,
        AudioClipService audioClipService,
        ILogger<MovieSessionService> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _database = database;
        _gladiaService = gladiaService;
        _analysisService = analysisService;
        _audioClipService = audioClipService;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<MovieSession> CreateSessionFromFolder(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath);
        
        // Try to extract date from folder name (not required since dates in folder names aren't reliable)
        var dateMatch = Regex.Match(folderName, @"(\d{4})[-_](\d{2})[-_](\d{2})");
        DateTime sessionDate;
        
        if (dateMatch.Success)
        {
            try
            {
                sessionDate = new DateTime(
                    int.Parse(dateMatch.Groups[1].Value),
                    int.Parse(dateMatch.Groups[2].Value),
                    int.Parse(dateMatch.Groups[3].Value)
                );
                _logger.LogInformation("Extracted date {Date} from folder name", sessionDate.ToShortDateString());
            }
            catch
            {
                sessionDate = DateTime.Now;
                _logger.LogWarning("Invalid date in folder name, using current date");
            }
        }
        else
        {
            sessionDate = DateTime.Now;
            _logger.LogDebug("No date found in folder name, using current date");
        }

        // Extract movie title from folder name
        var movieTitle = SuggestMovieTitle(folderName);

        var session = new MovieSession
        {
            Date = sessionDate,
            MovieTitle = movieTitle,
            FolderPath = folderPath,
            Status = ProcessingStatus.Pending
        };

        // Scan for audio files
        await ScanAudioFiles(session);
        
        // Determine participants
        DetermineParticipants(session);

        // Save to database - CLEAN API!
        await _database.UpsertAsync(session);

        _logger.LogInformation("Created movie session {SessionId} for {MovieTitle} on {Date}", 
            session.Id, session.MovieTitle, session.Date);

        return session;
    }

    private string SuggestMovieTitle(string folderName)
    {
        // Remove common separators and clean up
        var title = folderName
            .Replace("_", " ")
            .Replace("-", " ")
            .Replace(".", " ");

        // Remove date patterns (they're not reliable per requirements)
        title = Regex.Replace(title, @"\b\d{4}[-_]\d{2}[-_]\d{2}\b", "").Trim();
        title = Regex.Replace(title, @"\b\d{2}[-_]\d{2}[-_]\d{4}\b", "").Trim();

        // Normalize multiple spaces
        title = Regex.Replace(title, @"\s+", " ");

        // Title case
        title = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(title.ToLower());

        return !string.IsNullOrWhiteSpace(title) ? title : folderName;
    }

    private async Task ScanAudioFiles(MovieSession session)
    {
        var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma",
            ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp"
        };

        var files = Directory.GetFiles(session.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        var identifiedFiles = new HashSet<string>();

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var fileNameUpper = fileName.ToUpper();
            var fileInfo = new FileInfo(filePath);
            
            var audioFile = new AudioFile
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileInfo.Length,
                Duration = await GetMediaDuration(filePath)
            };

            // Check for MIC1-6 pattern
            var micMatch = Regex.Match(fileNameUpper, @"^MIC(\d)\.WAV$");
            if (micMatch.Success)
            {
                audioFile.SpeakerNumber = int.Parse(micMatch.Groups[1].Value);
                identifiedFiles.Add(fileName);
            }
            // Check for existing speaker pattern
            else if (Regex.IsMatch(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase))
            {
                var match = Regex.Match(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase);
                audioFile.SpeakerNumber = int.Parse(match.Groups[1].Value);
                identifiedFiles.Add(fileName);
            }
            // Skip PHONE.WAV and SOUND_PAD.WAV files (they're not speakers)
            else if (fileNameUpper == "PHONE.WAV" || fileNameUpper == "SOUND_PAD.WAV" || fileNameUpper == "SOUNDPAD.WAV")
            {
                identifiedFiles.Add(fileName);
                _logger.LogDebug("Identified {FileType} file: {FileName}", 
                    fileNameUpper.Contains("PHONE") ? "phone" : "sound pad", fileName);
            }
            // Check for master recording with timestamp pattern (e.g., 2024_1122_1839.wav)
            else if (Regex.IsMatch(fileName, @"^\d{4}_\d{4}_\d{4}\.(wav|mp3|m4a|aac|ogg|flac)$", RegexOptions.IgnoreCase))
            {
                audioFile.IsMasterRecording = true;
                identifiedFiles.Add(fileName);
                _logger.LogInformation("Identified timestamped master mix file: {FileName}", fileName);
            }
            // Check for master recording patterns
            else if (fileName.ToLower().Contains("master") || 
                     fileName.ToLower().Contains("combined") ||
                     fileName.ToLower().Contains("full") ||
                     fileName.ToLower().Contains("group"))
            {
                audioFile.IsMasterRecording = true;
                identifiedFiles.Add(fileName);
            }

            session.AudioFiles.Add(audioFile);
        }

        // Any unidentified file is likely the master mix (critical file)
        var unidentifiedFiles = session.AudioFiles.Where(f => !identifiedFiles.Contains(f.FileName)).ToList();
        if (unidentifiedFiles.Count == 1)
        {
            unidentifiedFiles[0].IsMasterRecording = true;
            _logger.LogInformation("Identified master mix file by elimination: {FileName}", unidentifiedFiles[0].FileName);
        }
        else if (unidentifiedFiles.Count > 1)
        {
            // If multiple unidentified files, pick the largest as master mix
            var largestFile = unidentifiedFiles.OrderByDescending(f => f.FileSize).First();
            largestFile.IsMasterRecording = true;
            _logger.LogWarning("Multiple unidentified files. Selected {FileName} as master mix based on size", largestFile.FileName);
        }

        // Log warning if no master recording found
        if (!session.AudioFiles.Any(f => f.IsMasterRecording))
        {
            _logger.LogError("CRITICAL: No master mix file identified in session {SessionId}", session.Id);
        }
    }

    private void DetermineParticipants(MovieSession session)
    {
        // Based on audio files, determine who was present
        var presentSpeakers = session.AudioFiles
            .Where(f => f.SpeakerNumber.HasValue)
            .Select(f => f.SpeakerNumber!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // All possible speakers (1-6)
        var allSpeakers = Enumerable.Range(1, 6).ToList();
        var absentSpeakers = allSpeakers.Except(presentSpeakers).ToList();

        session.ParticipantsPresent = presentSpeakers.Select(GetParticipantName).ToList();
        session.ParticipantsAbsent = absentSpeakers.Select(GetParticipantName).ToList();
    }

    public static string GetParticipantName(int micNumber)
    {
        return MicParticipantMap.TryGetValue(micNumber, out var name) ? name : $"Speaker{micNumber}";
    }

    private async Task<TimeSpan?> GetMediaDuration(string filePath)
    {
        try
        {
            using var reader = new MediaFoundationReader(filePath);
            return reader.TotalTime;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get duration for file {FilePath}", filePath);
            return null;
        }
    }

    public async Task ProcessSession(string sessionId, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        var session = await _database.GetByIdAsync<MovieSession>(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        try
        {
            // Step 1: Validation
            progressCallback?.Invoke(ProcessingStatus.Validating, 10, "Validating session data");
            session.Status = ProcessingStatus.Validating;
            await _database.UpsertAsync(session);

            if (!session.AudioFiles.Any())
            {
                throw new Exception("No audio files found in session");
            }

            // Step 2: Transcription
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 20, "Starting transcription");
            session.Status = ProcessingStatus.Transcribing;
            await _database.UpsertAsync(session);

            var audioFilePaths = session.AudioFiles.Select(f => f.FilePath).ToList();
            var transcriptionResults = await _gladiaService.ProcessMultipleFilesAsync(audioFilePaths, 
                (fileName, current, total) => 
                {
                    var progress = 20 + (int)((double)current / total * 50); // 20-70%
                    progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, $"Transcribing {fileName}");
                });

            // Update audio files with transcription data
            for (int i = 0; i < session.AudioFiles.Count; i++)
            {
                var audioFile = session.AudioFiles[i];
                var transcriptionResult = transcriptionResults.FirstOrDefault(r => r.source_file_path == audioFile.FilePath);
                
                if (transcriptionResult?.result?.transcription?.full_transcript != null)
                {
                    audioFile.TranscriptId = transcriptionResult.id;
                    audioFile.TranscriptText = transcriptionResult.result.transcription.full_transcript;
                }
            }

            // Step 3: AI Analysis
            progressCallback?.Invoke(ProcessingStatus.Analyzing, 75, "Analyzing transcripts for entertainment moments");
            session.Status = ProcessingStatus.Analyzing;
            await _database.UpsertAsync(session);

            await AnalyzeSession(session);

            // Step 4: Complete
            progressCallback?.Invoke(ProcessingStatus.Complete, 100, "Processing complete");
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;

            await _database.UpsertAsync(session);
            
            _logger.LogInformation("Successfully processed session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session {SessionId}", sessionId);
            
            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            await _database.UpsertAsync(session);
            
            throw;
        }
    }

    private async Task AnalyzeSession(MovieSession session)
    {
        // Check if we have transcripts to analyze
        var transcriptsAvailable = session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText));
        
        if (!transcriptsAvailable)
        {
            throw new Exception("No transcripts available for analysis");
        }

        try
        {
            // Use the AI analysis service to analyze the session
            session.CategoryResults = await _analysisService.AnalyzeSessionAsync(session);
            
            // Generate session stats based on the analysis results
            session.SessionStats = _analysisService.GenerateSessionStats(session, session.CategoryResults);
            
            // Generate audio clips for Top 5 lists
            await GenerateAudioClips(session);
            
            _logger.LogInformation("Successfully analyzed session {SessionId} with {HighlightCount} highlights", 
                session.Id, session.SessionStats.HighlightMoments);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI analysis failed for session {SessionId}, using fallback analysis", session.Id);
            
            // Fallback to basic analysis if AI analysis fails
            session.CategoryResults = CreateFallbackAnalysis(session);
            session.SessionStats = CreateFallbackStats(session);
        }
    }

    private CategoryResults CreateFallbackAnalysis(MovieSession session)
    {
        // Simple fallback analysis when AI analysis fails
        return new CategoryResults
        {
            BestJoke = CreateMockCategoryWinner("Speaker1", "15:23", "[Analysis unavailable - transcript processed but AI analysis failed]"),
            HottestTake = CreateMockCategoryWinner("Speaker2", "8:45", "[Analysis unavailable - please check API configuration]"),
        };
    }

    private SessionStats CreateFallbackStats(MovieSession session)
    {
        return new SessionStats
        {
            TotalDuration = CalculateTotalDuration(session),
            EnergyLevel = EnergyLevel.Medium,
            TechnicalQuality = AssessTechnicalQuality(session),
            HighlightMoments = 2, // Only mock highlights
            BestMomentsSummary = "Analysis completed with basic processing due to AI analysis error.",
            AttendancePattern = $"{session.ParticipantsPresent.Count}/{session.ParticipantsPresent.Count + session.ParticipantsAbsent.Count} regular members present"
        };
    }

    private string AssessTechnicalQuality(MovieSession session)
    {
        var totalFiles = session.AudioFiles.Count;
        if (totalFiles == 0) return "Unknown";

        var clearFiles = session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText));
        var percentage = (double)clearFiles / totalFiles * 100;

        return percentage switch
        {
            >= 90 => "Excellent - all audio clear",
            >= 70 => "Good - most audio clear",
            >= 50 => "Fair - some audio issues",
            _ => "Poor - significant audio problems"
        };
    }

    private CategoryWinner CreateMockCategoryWinner(string speaker, string timestamp, string quote)
    {
        return new CategoryWinner
        {
            Speaker = speaker,
            Timestamp = timestamp,
            Quote = quote,
            Setup = "During discussion of the movie's technical aspects",
            GroupReaction = "Loud laughter and agreement from most participants",
            WhyItsGreat = "Perfect timing and relatable criticism that resonated with the group",
            AudioQuality = AudioQuality.Clear,
            EntertainmentScore = new Random().Next(6, 10)
        };
    }

    private string CalculateTotalDuration(MovieSession session)
    {
        var totalMinutes = session.AudioFiles
            .Where(f => f.Duration.HasValue)
            .Select(f => f.Duration!.Value.TotalMinutes)
            .Max(); // Take the longest recording (likely the master)

        if (totalMinutes >= 60)
        {
            var hours = (int)(totalMinutes / 60);
            var minutes = (int)(totalMinutes % 60);
            return $"{hours}h {minutes}m";
        }
        else
        {
            return $"{(int)totalMinutes}m";
        }
    }

    private async Task GenerateAudioClips(MovieSession session)
    {
        try
        {
            var clipsGenerated = 0;
            
            // Generate clips for Funniest Sentences
            if (session.CategoryResults.FunniestSentences?.Entries.Any() == true)
            {
                await PopulateSourceAudioFiles(session.CategoryResults.FunniestSentences, session);
                var clipUrls = await _audioClipService.GenerateClipsForTopFiveAsync(session, session.CategoryResults.FunniestSentences);
                clipsGenerated += clipUrls.Count;
            }
            
            // Generate clips for Most Bland Comments
            if (session.CategoryResults.MostBlandComments?.Entries.Any() == true)
            {
                await PopulateSourceAudioFiles(session.CategoryResults.MostBlandComments, session);
                var clipUrls = await _audioClipService.GenerateClipsForTopFiveAsync(session, session.CategoryResults.MostBlandComments);
                clipsGenerated += clipUrls.Count;
            }
            
            _logger.LogInformation("Generated {ClipCount} audio clips for session {SessionId}", clipsGenerated, session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate audio clips for session {SessionId}", session.Id);
        }
    }

    private async Task PopulateSourceAudioFiles(TopFiveList topFive, MovieSession session)
    {
        foreach (var entry in topFive.Entries)
        {
            if (string.IsNullOrEmpty(entry.SourceAudioFile))
            {
                // If no specific source file is identified, use the master recording or first available
                var masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording)?.FileName;
                if (!string.IsNullOrEmpty(masterFile))
                {
                    entry.SourceAudioFile = masterFile;
                }
                else if (session.AudioFiles.Any())
                {
                    entry.SourceAudioFile = session.AudioFiles.First().FileName;
                }
            }
            
            // Convert timestamp to seconds if needed
            if (entry.StartTimeSeconds == 0 && !string.IsNullOrEmpty(entry.Timestamp))
            {
                var timeSpan = _audioClipService.ParseTimestamp(entry.Timestamp);
                entry.StartTimeSeconds = timeSpan.TotalSeconds;
                
                // Add some reasonable duration (e.g., 5-8 seconds) if end time not specified
                if (entry.EndTimeSeconds == 0)
                {
                    entry.EndTimeSeconds = entry.StartTimeSeconds + 6; // Default 6 second clip
                }
            }
        }
    }

    public async Task<List<MovieSession>> GetAllSessions()
    {
        return await _database.GetAllAsync<MovieSession>();
    }

    public async Task<List<MovieSession>> GetRecentSessions(int limit = 10)
    {
        // Use the new paging API for better performance!
        var (sessions, _) = await _database.GetPagedAsync<MovieSession>(
            page: 1,
            pageSize: limit,
            orderBy: s => s.CreatedAt,
            descending: true
        );
        
        return sessions;
    }

    public async Task<MovieSession?> GetSession(string sessionId)
    {
        return await _database.GetByIdAsync<MovieSession>(sessionId);
    }

    public async Task<bool> DeleteSession(string sessionId)
    {
        return await _database.DeleteByIdAsync<MovieSession>(sessionId);
    }

    public async Task<List<MovieSession>> SearchSessions(string searchTerm)
    {
        // Use the new text search API for better performance!
        return await _database.SearchTextAsync<MovieSession>(
            searchTerm,
            s => s.MovieTitle,
            s => s.FolderPath
        );
    }

    // Additional convenience methods using the new API
    public async Task<long> GetSessionCount()
    {
        return await _database.CountAsync<MovieSession>();
    }

    public async Task<bool> HasAnySessions()
    {
        return await _database.AnyAsync<MovieSession>();
    }

    public async Task<List<MovieSession>> GetSessionsByDateRange(DateTime start, DateTime end)
    {
        return await _database.FindAsync<MovieSession>(s => s.Date >= start && s.Date <= end);
    }

    public async Task<List<MovieSession>> GetFailedSessions()
    {
        return await _database.FindAsync<MovieSession>(s => s.Status == ProcessingStatus.Failed);
    }
}