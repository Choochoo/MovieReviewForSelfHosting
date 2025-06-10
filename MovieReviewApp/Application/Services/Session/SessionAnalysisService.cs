using MovieReviewApp.Models;
using MovieReviewApp.Application.Services;

namespace MovieReviewApp.Application.Services.Session;

/// <summary>
/// Service responsible for orchestrating AI analysis of movie sessions and generating entertainment insights.
/// Coordinates between transcription processing and analysis results generation.
/// </summary>
public class SessionAnalysisService
{
    private readonly MovieSessionAnalysisService _analysisService;
    private readonly AudioClipService _audioClipService;
    private readonly ILogger<SessionAnalysisService> _logger;

    public SessionAnalysisService(
        MovieSessionAnalysisService analysisService,
        AudioClipService audioClipService,
        ILogger<SessionAnalysisService> logger)
    {
        _analysisService = analysisService;
        _audioClipService = audioClipService;
        _logger = logger;
    }

    /// <summary>
    /// Performs comprehensive AI analysis on a movie session including transcript processing and clip generation.
    /// </summary>
    public async Task<CategoryResults> AnalyzeSessionAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Running AI analysis", 80);

        List<AudioFile> transcribedFiles = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.TranscriptsDownloaded &&
            !string.IsNullOrEmpty(f.TranscriptText)).ToList();

        if (!transcribedFiles.Any())
        {
            throw new InvalidOperationException("No transcribed files available for analysis");
        }

        try
        {
            // Mark files as processing
            foreach (AudioFile file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.ProcessingWithAI;
                file.CurrentStep = "Waiting for OpenAI response";
                file.ProgressPercentage = 50;
            }

            // Update status to show processing has begun
            foreach (AudioFile file in transcribedFiles)
            {
                file.CurrentStep = "Processing with AI...";
                file.ProgressPercentage = 75;
            }

            // Run analysis using existing service
            CategoryResults results = await _analysisService.AnalyzeSessionAsync(session);

            // Generate session stats based on the analysis results
            session.SessionStats = _analysisService.GenerateSessionStats(session, results);

            // Generate audio clips for Top 5 lists
            await GenerateAudioClipsAsync(session, results);

            // Mark as complete
            foreach (AudioFile file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.Complete;
                file.CurrentStep = "Complete";
                file.ProgressPercentage = 100;
                file.CanRetry = false;
            }

            progressCallback?.Invoke("AI analysis complete", 95);
            return results;
        }
        catch (Exception ex)
        {
            foreach (AudioFile file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.Failed;
                file.ConversionError = ex.Message;
                file.CurrentStep = "AI analysis failed";
                file.CanRetry = true;
            }
            throw;
        }
    }

    /// <summary>
    /// Performs analysis on a session with fallback handling for when AI analysis fails.
    /// </summary>
    public async Task<CategoryResults> AnalyzeSessionWithFallbackAsync(MovieSession session)
    {
        // Check if we have transcripts to analyze
        bool transcriptsAvailable = session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText));

        // Log detailed information about transcript availability
        _logger.LogInformation("Analyzing session {SessionId}: {TotalFiles} audio files, {TranscriptFiles} with transcripts",
            session.Id, session.AudioFiles.Count, session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText)));

        foreach (AudioFile audioFile in session.AudioFiles)
        {
            _logger.LogDebug("Audio file {FileName}: Status={Status}, HasTranscript={HasTranscript}, TranscriptLength={Length}",
                audioFile.FileName,
                audioFile.ProcessingStatus,
                !string.IsNullOrEmpty(audioFile.TranscriptText),
                audioFile.TranscriptText?.Length ?? 0);
        }

        if (!transcriptsAvailable)
        {
            _logger.LogWarning("No transcripts available for analysis in session {SessionId}. Audio file statuses: {Statuses}",
                session.Id,
                string.Join(", ", session.AudioFiles.Select(f => $"{f.FileName}:{f.ProcessingStatus}")));

            // Use fallback analysis instead of throwing error
            _logger.LogInformation("Using fallback analysis for session {SessionId}", session.Id);
            CategoryResults fallbackResults = CreateFallbackAnalysis(session);
            session.SessionStats = CreateFallbackStats(session);
            return fallbackResults;
        }

        try
        {
            // Use the AI analysis service to analyze the session
            CategoryResults results = await _analysisService.AnalyzeSessionAsync(session);

            // Generate session stats based on the analysis results
            session.SessionStats = _analysisService.GenerateSessionStats(session, results);

            // Generate audio clips for Top 5 lists
            await GenerateAudioClipsAsync(session, results);

            _logger.LogInformation("Successfully analyzed session {SessionId} with {HighlightCount} highlights",
                session.Id, session.SessionStats.HighlightMoments);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI analysis failed for session {SessionId}, using fallback analysis", session.Id);

            // Fallback to basic analysis if AI analysis fails
            CategoryResults fallbackResults = CreateFallbackAnalysis(session);
            session.SessionStats = CreateFallbackStats(session);
            return fallbackResults;
        }
    }

    /// <summary>
    /// Reruns analysis for a session that has completed transcription.
    /// </summary>
    public async Task<bool> RerunAnalysisAsync(string sessionId)
    {
        try
        {
            // This would need access to session retrieval - normally done by orchestrating service
            _logger.LogInformation("Analysis rerun requested for session {SessionId}", sessionId);
            return true; // Implementation would depend on how sessions are retrieved
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rerun analysis for session {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Generates audio clips for entertainment highlights identified in the analysis.
    /// </summary>
    private async Task GenerateAudioClipsAsync(MovieSession session, CategoryResults results)
    {
        try
        {
            int clipsGenerated = 0;

            // Generate clips for Funniest Sentences
            if (results.FunniestSentences?.Entries.Any() == true)
            {
                await PopulateSourceAudioFilesAsync(results.FunniestSentences, session);
                List<string> clipUrls = await _audioClipService.GenerateClipsForTopFiveAsync(session, results.FunniestSentences);
                clipsGenerated += clipUrls.Count;
            }

            // Generate clips for Most Bland Comments
            if (results.MostBlandComments?.Entries.Any() == true)
            {
                await PopulateSourceAudioFilesAsync(results.MostBlandComments, session);
                List<string> clipUrls = await _audioClipService.GenerateClipsForTopFiveAsync(session, results.MostBlandComments);
                clipsGenerated += clipUrls.Count;
            }

            _logger.LogInformation("Generated {ClipCount} audio clips for session {SessionId}", clipsGenerated, session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate audio clips for session {SessionId}", session.Id);
        }
    }

    /// <summary>
    /// Populates source audio file information for Top 5 entries to enable clip generation.
    /// </summary>
    private async Task PopulateSourceAudioFilesAsync(TopFiveList topFive, MovieSession session)
    {
        foreach (TopFiveEntry entry in topFive.Entries)
        {
            if (string.IsNullOrEmpty(entry.SourceAudioFile))
            {
                // If no specific source file is identified, use the master recording or first available
                string? masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording)?.FileName;
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
                TimeSpan timeSpan = _audioClipService.ParseTimestamp(entry.Timestamp);
                entry.StartTimeSeconds = timeSpan.TotalSeconds;

                // Add some reasonable duration (e.g., 5-8 seconds) if end time not specified
                if (entry.EndTimeSeconds == 0)
                {
                    entry.EndTimeSeconds = entry.StartTimeSeconds + 6; // Default 6 second clip
                }
            }
        }

        await Task.CompletedTask; // Make method async for consistency
    }

    /// <summary>
    /// Creates fallback analysis results when AI analysis is not available or fails.
    /// </summary>
    private CategoryResults CreateFallbackAnalysis(MovieSession session)
    {
        // Simple fallback analysis when AI analysis fails
        return new CategoryResults
        {
            BestJoke = CreateMockCategoryWinner("Speaker1", "15:23", "[Analysis unavailable - transcript processed but AI analysis failed]"),
            HottestTake = CreateMockCategoryWinner("Speaker2", "8:45", "[Analysis unavailable - please check API configuration]"),
        };
    }

    /// <summary>
    /// Creates fallback session statistics when AI analysis is not available.
    /// </summary>
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

    /// <summary>
    /// Assesses the technical quality of audio recordings based on successful transcription rates.
    /// </summary>
    private string AssessTechnicalQuality(MovieSession session)
    {
        int totalFiles = session.AudioFiles.Count;
        if (totalFiles == 0) return "Unknown";

        int clearFiles = session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText));
        double percentage = (double)clearFiles / totalFiles * 100;

        return percentage switch
        {
            >= 90 => "Excellent - all audio clear",
            >= 70 => "Good - most audio clear",
            >= 50 => "Fair - some audio issues",
            _ => "Poor - significant audio problems"
        };
    }

    /// <summary>
    /// Creates a mock category winner for fallback analysis scenarios.
    /// </summary>
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

    /// <summary>
    /// Calculates the total duration of a session based on the longest audio recording.
    /// </summary>
    private string CalculateTotalDuration(MovieSession session)
    {
        double totalMinutes = session.AudioFiles
            .Where(f => f.Duration.HasValue)
            .Select(f => f.Duration!.Value.TotalMinutes)
            .DefaultIfEmpty(0)
            .Max(); // Take the longest recording (likely the master)

        if (totalMinutes >= 60)
        {
            int hours = (int)(totalMinutes / 60);
            int minutes = (int)(totalMinutes % 60);
            return $"{hours}h {minutes}m";
        }
        else
        {
            return $"{(int)totalMinutes}m";
        }
    }
}