using MovieReviewApp.Models;
using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Infrastructure.Services;

namespace MovieReviewApp.Application.Services.Session;

/// <summary>
/// Service responsible for maintenance operations on movie sessions including recovery, cleanup, and diagnostics.
/// Handles stuck sessions, failed processing recovery, and system health checks.
/// </summary>
public class SessionMaintenanceService
{
    private readonly IDatabaseService _database;
    private readonly GladiaService _gladiaService;
    private readonly ILogger<SessionMaintenanceService> _logger;

    public SessionMaintenanceService(
        IDatabaseService database,
        GladiaService gladiaService,
        ILogger<SessionMaintenanceService> logger)
    {
        _database = database;
        _gladiaService = gladiaService;
        _logger = logger;
    }

    /// <summary>
    /// Fixes sessions that are stuck in analyzing status by checking their actual completion state.
    /// </summary>
    public async Task<int> FixStuckAnalyzingSessionsAsync()
    {
        try
        {
            List<MovieSession> analyzingSessions = await _database.FindAsync<MovieSession>(s => s.Status == ProcessingStatus.Analyzing);
            int fixedCount = 0;

            foreach (MovieSession session in analyzingSessions)
            {
                bool hasTranscripts = session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText));
                bool hasAnalysis = session.CategoryResults != null;

                if (hasTranscripts && hasAnalysis)
                {
                    // Session is actually complete, just fix the status
                    session.Status = ProcessingStatus.Complete;
                    session.ProcessedAt = DateTime.UtcNow;
                    await _database.UpsertAsync(session);
                    fixedCount++;
                    _logger.LogInformation("Fixed stuck analyzing session: {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);
                }
                else if (hasTranscripts)
                {
                    // Reset to allow re-analysis
                    session.Status = ProcessingStatus.Transcribing;
                    session.ErrorMessage = "Session was stuck in analyzing status and has been reset for re-analysis";
                    await _database.UpsertAsync(session);
                    fixedCount++;
                    _logger.LogInformation("Reset stuck session for re-analysis: {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);
                }
            }

            if (fixedCount > 0)
            {
                _logger.LogInformation("Fixed {FixedCount} stuck analyzing sessions", fixedCount);
            }

            return fixedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fix stuck analyzing sessions");
            return 0;
        }
    }

    /// <summary>
    /// Finds and fixes sessions that may be stuck in processing for an extended period.
    /// </summary>
    public async Task<int> FixStuckProcessingSessionsAsync(TimeSpan stuckThreshold)
    {
        try
        {
            DateTime cutoffTime = DateTime.UtcNow.Subtract(stuckThreshold);
            List<MovieSession> stuckSessions = await _database.FindAsync<MovieSession>(s => 
                (s.Status == ProcessingStatus.Transcribing || 
                 s.Status == ProcessingStatus.Analyzing ||
                 s.Status == ProcessingStatus.Validating) && 
                s.CreatedAt < cutoffTime);

            int fixedCount = 0;
            foreach (MovieSession session in stuckSessions)
            {
                try
                {
                    _logger.LogInformation("Fixing stuck session {SessionId} - {MovieTitle} (Status: {Status}, Created: {Created})", 
                        session.Id, session.MovieTitle, session.Status, session.CreatedAt);
                    
                    // Reset to a recoverable state
                    ProcessingStatus originalStatus = session.Status;
                    session.Status = ProcessingStatus.Pending;
                    session.ErrorMessage = $"Session was stuck in {originalStatus} status for over {stuckThreshold.TotalMinutes:F0} minutes and has been reset";
                    
                    await _database.UpsertAsync(session);
                    fixedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fix stuck session {SessionId}", session.Id);
                }
            }

            if (fixedCount > 0)
            {
                _logger.LogInformation("Fixed {FixedCount} stuck processing sessions", fixedCount);
            }

            return fixedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fix stuck processing sessions");
            return 0;
        }
    }

    /// <summary>
    /// Attempts to recover failed audio file processing by retrying specific steps.
    /// </summary>
    public async Task<int> RecoverFailedAudioFilesAsync(string sessionId)
    {
        try
        {
            MovieSession? session = await _database.GetByIdAsync<MovieSession>(sessionId);
            if (session == null)
            {
                _logger.LogWarning("Session {SessionId} not found for audio file recovery", sessionId);
                return 0;
            }

            List<AudioFile> failedFiles = session.AudioFiles.Where(f => 
                f.ProcessingStatus == AudioProcessingStatus.Failed ||
                f.ProcessingStatus == AudioProcessingStatus.FailedMp3).ToList();

            if (!failedFiles.Any())
            {
                _logger.LogInformation("No failed audio files found in session {SessionId}", sessionId);
                return 0;
            }

            int recoveredCount = 0;
            foreach (AudioFile audioFile in failedFiles)
            {
                try
                {
                    // Reset file status for retry
                    audioFile.ProcessingStatus = AudioProcessingStatus.Pending;
                    audioFile.ConversionError = null;
                    audioFile.CurrentStep = "Ready for retry";
                    audioFile.ProgressPercentage = 0;
                    audioFile.CanRetry = true;
                    audioFile.LastUpdated = DateTime.UtcNow;

                    recoveredCount++;
                    _logger.LogInformation("Reset failed audio file {FileName} for retry in session {SessionId}", 
                        audioFile.FileName, sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reset audio file {FileName} in session {SessionId}", 
                        audioFile.FileName, sessionId);
                }
            }

            if (recoveredCount > 0)
            {
                // Reset session status to allow reprocessing
                session.Status = ProcessingStatus.Pending;
                session.ErrorMessage = $"Reset {recoveredCount} failed audio files for retry";
                
                await _database.UpsertAsync(session);
                _logger.LogInformation("Recovered {RecoveredCount} failed audio files in session {SessionId}", 
                    recoveredCount, sessionId);
            }

            return recoveredCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover failed audio files for session {SessionId}", sessionId);
            return 0;
        }
    }

    /// <summary>
    /// Redownloads existing transcriptions from Gladia for sessions that have transcript IDs but missing content.
    /// </summary>
    public async Task<int> RedownloadExistingTranscriptionsAsync(string sessionId, Action<string, int, int>? progressCallback = null)
    {
        try
        {
            MovieSession? session = await _database.GetByIdAsync<MovieSession>(sessionId);
            if (session == null)
            {
                _logger.LogWarning("Session {SessionId} not found for transcription redownload", sessionId);
                return 0;
            }

            List<AudioFile> transcribedFiles = session.AudioFiles.Where(f => !string.IsNullOrEmpty(f.TranscriptId)).ToList();

            if (!transcribedFiles.Any())
            {
                _logger.LogWarning("No transcribed files found in session {SessionId}", sessionId);
                return 0;
            }

            _logger.LogInformation("Redownloading {Count} transcriptions for session {SessionId}", transcribedFiles.Count, sessionId);

            int successCount = 0;
            int currentIndex = 0;

            foreach (AudioFile audioFile in transcribedFiles)
            {
                currentIndex++;
                progressCallback?.Invoke($"Redownloading {audioFile.FileName}", currentIndex, transcribedFiles.Count);

                try
                {
                    // Get the transcription result from Gladia
                    dynamic result = await _gladiaService.GetTranscriptionResultAsync(audioFile.TranscriptId!);

                    // Save the JSON file alongside the audio file
                    string? audioDirectory = Path.GetDirectoryName(audioFile.FilePath);
                    if (!string.IsNullOrEmpty(audioDirectory))
                    {
                        string jsonPath = await _gladiaService.SaveTranscriptionJsonAsync(result, audioFile.FilePath, audioDirectory);
                        audioFile.JsonFilePath = jsonPath;

                        // Also update the transcript text if needed
                        if (string.IsNullOrEmpty(audioFile.TranscriptText))
                        {
                            string? rawTranscript = result.result?.transcription?.full_transcript;
                            audioFile.TranscriptText = !string.IsNullOrEmpty(rawTranscript) && session.MicAssignments != null
                                ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, session.MicAssignments, audioFile.FileName)
                                : rawTranscript;
                        }

                        successCount++;
                        _logger.LogInformation("Successfully redownloaded transcription for {FileName}, saved to {JsonPath}",
                            audioFile.FileName, jsonPath);
                    }
                    else
                    {
                        _logger.LogWarning("Could not determine directory for {FileName}", audioFile.FileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to redownload transcription for {FileName} (TranscriptId: {TranscriptId})",
                        audioFile.FileName, audioFile.TranscriptId);
                }
            }

            // Save the updated session to persist JsonFilePath updates
            await _database.UpsertAsync(session);

            _logger.LogInformation("Redownload complete. {SuccessCount}/{TotalCount} transcriptions recovered",
                successCount, transcribedFiles.Count);

            return successCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to redownload transcriptions for session {SessionId}", sessionId);
            return 0;
        }
    }

    /// <summary>
    /// Performs comprehensive diagnostics on a movie session to identify issues and potential fixes.
    /// </summary>
    public async Task<SessionDiagnostics> DiagnoseSessionAsync(string sessionId)
    {
        try
        {
            MovieSession? session = await _database.GetByIdAsync<MovieSession>(sessionId);
            if (session == null)
            {
                return new SessionDiagnostics
                {
                    SessionFound = false,
                    Issues = new List<string> { "Session not found in database" }
                };
            }

            SessionDiagnostics diagnostics = new SessionDiagnostics
            {
                SessionFound = true,
                SessionId = sessionId,
                MovieTitle = session.MovieTitle,
                Status = session.Status,
                AudioFileCount = session.AudioFiles.Count,
                Issues = new List<string>(),
                Recommendations = new List<string>()
            };

            // Check folder existence
            if (!string.IsNullOrEmpty(session.FolderPath) && !Directory.Exists(session.FolderPath))
            {
                diagnostics.Issues.Add($"Session folder does not exist: {session.FolderPath}");
                diagnostics.Recommendations.Add("Verify the folder path or update session with correct path");
            }

            // Check audio files
            foreach (AudioFile audioFile in session.AudioFiles)
            {
                if (!File.Exists(audioFile.FilePath))
                {
                    diagnostics.Issues.Add($"Audio file missing: {audioFile.FileName}");
                    diagnostics.Recommendations.Add($"Restore missing file: {audioFile.FilePath}");
                }

                if (audioFile.ProcessingStatus == AudioProcessingStatus.Failed)
                {
                    diagnostics.Issues.Add($"Failed audio file: {audioFile.FileName} - {audioFile.ConversionError}");
                    diagnostics.Recommendations.Add($"Use RecoverFailedAudioFiles to retry processing for {audioFile.FileName}");
                }

                if (!string.IsNullOrEmpty(audioFile.TranscriptId) && string.IsNullOrEmpty(audioFile.TranscriptText))
                {
                    diagnostics.Issues.Add($"Missing transcript text for {audioFile.FileName} (has TranscriptId)");
                    diagnostics.Recommendations.Add($"Use RedownloadExistingTranscriptions to recover transcript for {audioFile.FileName}");
                }
            }

            // Check session status consistency
            if (session.Status == ProcessingStatus.Complete)
            {
                if (session.CategoryResults == null)
                {
                    diagnostics.Issues.Add("Session marked complete but missing analysis results");
                    diagnostics.Recommendations.Add("Use RerunAnalysis to regenerate missing analysis");
                }

                if (!session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText)))
                {
                    diagnostics.Issues.Add("Session marked complete but no transcript text available");
                    diagnostics.Recommendations.Add("Check transcription status and reprocess if needed");
                }
            }

            // Check for stuck processing
            if ((session.Status == ProcessingStatus.Analyzing || session.Status == ProcessingStatus.Transcribing) &&
                session.CreatedAt < DateTime.UtcNow.AddHours(-1))
            {
                diagnostics.Issues.Add($"Session appears stuck in {session.Status} status for over 1 hour");
                diagnostics.Recommendations.Add("Use FixStuckProcessingSessions to reset session status");
            }

            diagnostics.HealthScore = CalculateHealthScore(diagnostics);
            return diagnostics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to diagnose session {SessionId}", sessionId);
            return new SessionDiagnostics
            {
                SessionFound = false,
                Issues = new List<string> { $"Diagnostic error: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Calculates a health score for session diagnostics based on identified issues.
    /// </summary>
    private static double CalculateHealthScore(SessionDiagnostics diagnostics)
    {
        if (!diagnostics.SessionFound) return 0.0;

        double score = 100.0;
        
        // Deduct points for various issues
        score -= diagnostics.Issues.Count * 10; // 10 points per issue
        
        // Ensure score doesn't go below 0
        return Math.Max(0.0, score);
    }

    /// <summary>
    /// Performs bulk cleanup operations on failed or abandoned sessions.
    /// </summary>
    public async Task<CleanupSummary> CleanupAbandonedSessionsAsync(TimeSpan abandonedThreshold)
    {
        try
        {
            DateTime cutoffDate = DateTime.UtcNow.Subtract(abandonedThreshold);
            CleanupSummary summary = new CleanupSummary();

            // Find sessions that have been stuck in non-complete states for too long
            List<MovieSession> abandonedSessions = await _database.FindAsync<MovieSession>(s => 
                s.Status != ProcessingStatus.Complete && 
                s.Status != ProcessingStatus.Failed &&
                s.CreatedAt < cutoffDate);

            foreach (MovieSession session in abandonedSessions)
            {
                try
                {
                    // Check if session can be recovered or should be marked as failed
                    bool hasAnyTranscripts = session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText));
                    
                    if (hasAnyTranscripts)
                    {
                        // Try to recover by resetting to transcribing status
                        session.Status = ProcessingStatus.Transcribing;
                        session.ErrorMessage = $"Session was abandoned and recovered (created: {session.CreatedAt})";
                        summary.RecoveredSessions++;
                    }
                    else
                    {
                        // Mark as failed if no progress was made
                        session.Status = ProcessingStatus.Failed;
                        session.ErrorMessage = $"Session abandoned after {abandonedThreshold.TotalHours:F1} hours without progress";
                        summary.FailedSessions++;
                    }

                    await _database.UpsertAsync(session);
                    summary.ProcessedSessions++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to cleanup session {SessionId}", session.Id);
                    summary.ErrorSessions++;
                }
            }

            _logger.LogInformation("Cleanup completed: {ProcessedCount} processed, {RecoveredCount} recovered, {FailedCount} failed, {ErrorCount} errors",
                summary.ProcessedSessions, summary.RecoveredSessions, summary.FailedSessions, summary.ErrorSessions);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup abandoned sessions");
            return new CleanupSummary { ErrorSessions = 1 };
        }
    }
}

/// <summary>
/// Represents diagnostic information about a movie session's health and potential issues.
/// </summary>
public class SessionDiagnostics
{
    public bool SessionFound { get; set; }
    public string? SessionId { get; set; }
    public string? MovieTitle { get; set; }
    public ProcessingStatus Status { get; set; }
    public int AudioFileCount { get; set; }
    public List<string> Issues { get; set; } = new List<string>();
    public List<string> Recommendations { get; set; } = new List<string>();
    public double HealthScore { get; set; }
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