using MovieReviewApp.Models;
using MovieReviewApp.Core.Interfaces;

namespace MovieReviewApp.Application.Services.Session;

/// <summary>
/// Main orchestration service that coordinates all movie session operations using specialized services.
/// Acts as the primary interface for session management, delegating to appropriate specialized services.
/// </summary>
public class SessionOrchestrationService
{
    private readonly SessionRepositoryService _repositoryService;
    private readonly SessionMetadataService _metadataService;
    private readonly AudioProcessingWorkflowService _audioWorkflowService;
    private readonly SessionAnalysisService _analysisService;
    private readonly SessionMaintenanceService _maintenanceService;
    private readonly ILogger<SessionOrchestrationService> _logger;

    public SessionOrchestrationService(
        SessionRepositoryService repositoryService,
        SessionMetadataService metadataService,
        AudioProcessingWorkflowService audioWorkflowService,
        SessionAnalysisService analysisService,
        SessionMaintenanceService maintenanceService,
        ILogger<SessionOrchestrationService> logger)
    {
        _repositoryService = repositoryService;
        _metadataService = metadataService;
        _audioWorkflowService = audioWorkflowService;
        _analysisService = analysisService;
        _maintenanceService = maintenanceService;
        _logger = logger;
    }

    #region Session Creation and Preparation

    /// <summary>
    /// Prepares a movie session from a folder containing audio files, extracting metadata and scanning for audio content.
    /// </summary>
    public async Task<MovieSession> PrepareSessionFromFolderAsync(string folderPath, Dictionary<int, string>? micAssignments = null)
    {
        _logger.LogInformation("Preparing session from folder: {FolderPath}", folderPath);
        return await _metadataService.PrepareSessionFromFolderAsync(folderPath, micAssignments);
    }

    /// <summary>
    /// Creates and saves a new movie session to the database.
    /// </summary>
    public async Task<MovieSession> CreateSessionAsync(MovieSession session)
    {
        _logger.LogInformation("Creating new session for {MovieTitle}", session.MovieTitle);
        return await _repositoryService.CreateSessionAsync(session);
    }

    /// <summary>
    /// Saves or updates a movie session in the database.
    /// </summary>
    public async Task<MovieSession> SaveSessionAsync(MovieSession session)
    {
        return await _repositoryService.UpdateSessionAsync(session);
    }

    #endregion

    #region Session Processing

    /// <summary>
    /// Processes a movie session through the complete enhanced workflow.
    /// </summary>
    public async Task<MovieSession> ProcessSessionEnhancedAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        try
        {
            _logger.LogInformation("Starting enhanced processing for session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);

            // Update session status and save
            session.Status = ProcessingStatus.Validating;
            await _repositoryService.UpdateSessionAsync(session);

            // Run audio processing workflow
            session = await _audioWorkflowService.ProcessSessionEnhancedAsync(session, progressCallback);
            await _repositoryService.UpdateSessionAsync(session);

            // Run AI analysis
            progressCallback?.Invoke("Running AI analysis", 80);
            session.Status = ProcessingStatus.Analyzing;
            await _repositoryService.UpdateSessionAsync(session);

            CategoryResults analysisResults = await _analysisService.AnalyzeSessionWithFallbackAsync(session);
            session.CategoryResults = analysisResults;

            // Mark as complete
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;
            await _repositoryService.UpdateSessionAsync(session);

            progressCallback?.Invoke("Processing complete", 100);
            _logger.LogInformation("Successfully completed enhanced processing for session {SessionId}", session.Id);

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enhanced processing failed for session {SessionId}", session.Id);
            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            await _repositoryService.UpdateSessionAsync(session);
            throw;
        }
    }

    /// <summary>
    /// Processes a movie session through the standard workflow with detailed progress tracking.
    /// </summary>
    public async Task ProcessSessionStandardAsync(MovieSession session, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        try
        {
            _logger.LogInformation("Starting standard processing for session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);

            // Step 1: Validation
            progressCallback?.Invoke(ProcessingStatus.Validating, 10, "Validating session data");
            session.Status = ProcessingStatus.Validating;
            await _repositoryService.UpdateSessionAsync(session);

            if (!session.AudioFiles.Any())
            {
                throw new Exception("No audio files found in session");
            }

            // Step 2: Audio Processing
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 20, "Starting audio processing");
            session.Status = ProcessingStatus.Transcribing;
            await _repositoryService.UpdateSessionAsync(session);

            await _audioWorkflowService.ProcessSessionStandardAsync(session, progressCallback);
            await _repositoryService.UpdateSessionAsync(session);

            // Step 3: AI Analysis
            progressCallback?.Invoke(ProcessingStatus.Analyzing, 75, "Analyzing transcripts for entertainment moments");
            session.Status = ProcessingStatus.Analyzing;
            await _repositoryService.UpdateSessionAsync(session);

            CategoryResults analysisResults = await _analysisService.AnalyzeSessionWithFallbackAsync(session);
            session.CategoryResults = analysisResults;

            // Step 4: Validation and Completion
            int successfulTranscripts = session.AudioFiles.Count(f =>
                f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete &&
                !string.IsNullOrEmpty(f.TranscriptText));

            int totalFiles = session.AudioFiles.Count;

            if (successfulTranscripts == 0)
            {
                throw new Exception($"No successful transcripts generated from {totalFiles} audio files. Session not saved.");
            }

            if (successfulTranscripts < totalFiles)
            {
                _logger.LogWarning("Only {SuccessCount}/{TotalCount} files successfully transcribed for session {SessionId}",
                    successfulTranscripts, totalFiles, session.Id);
            }

            // Final validation: ensure we have analysis results
            if (session.CategoryResults == null)
            {
                throw new Exception("Session analysis not completed. Session not saved.");
            }

            progressCallback?.Invoke(ProcessingStatus.Complete, 100, "Processing complete");
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;

            await _repositoryService.UpdateSessionAsync(session);

            _logger.LogInformation("Successfully processed session {SessionId} with {SuccessCount}/{TotalCount} successful transcripts",
                session.Id, successfulTranscripts, totalFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session {SessionId}", session.Id);
            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            await _repositoryService.UpdateSessionAsync(session);
            throw;
        }
    }

    /// <summary>
    /// Processes a movie session by ID through the complete pipeline.
    /// </summary>
    public async Task ProcessSessionByIdAsync(string sessionId, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        MovieSession? session = await _repositoryService.GetSessionAsync(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        await ProcessSessionStandardAsync(session, progressCallback);
    }

    #endregion

    #region Session Queries

    /// <summary>
    /// Retrieves all movie sessions from the database.
    /// </summary>
    public async Task<List<MovieSession>> GetAllSessionsAsync()
    {
        return await _repositoryService.GetAllSessionsAsync();
    }

    /// <summary>
    /// Retrieves the most recent movie sessions, ordered by date descending.
    /// </summary>
    public async Task<List<MovieSession>> GetRecentSessionsAsync(int limit = 10)
    {
        return await _repositoryService.GetRecentSessionsAsync(limit);
    }

    /// <summary>
    /// Retrieves a specific movie session by its unique identifier.
    /// </summary>
    public async Task<MovieSession?> GetSessionAsync(string sessionId)
    {
        return await _repositoryService.GetSessionAsync(sessionId);
    }

    /// <summary>
    /// Searches for movie sessions based on a search term.
    /// </summary>
    public async Task<List<MovieSession>> SearchSessionsAsync(string searchTerm)
    {
        return await _repositoryService.SearchSessionsAsync(searchTerm);
    }

    /// <summary>
    /// Gets the total count of movie sessions in the database.
    /// </summary>
    public async Task<long> GetSessionCountAsync()
    {
        return await _repositoryService.GetSessionCountAsync();
    }

    /// <summary>
    /// Determines if there are any movie sessions in the database.
    /// </summary>
    public async Task<bool> HasAnySessionsAsync()
    {
        return await _repositoryService.HasAnySessionsAsync();
    }

    /// <summary>
    /// Retrieves movie sessions within a specific date range.
    /// </summary>
    public async Task<List<MovieSession>> GetSessionsByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _repositoryService.GetSessionsByDateRangeAsync(start, end);
    }

    /// <summary>
    /// Retrieves all movie sessions that have failed processing.
    /// </summary>
    public async Task<List<MovieSession>> GetFailedSessionsAsync()
    {
        return await _repositoryService.GetFailedSessionsAsync();
    }

    /// <summary>
    /// Retrieves movie sessions by processing status.
    /// </summary>
    public async Task<List<MovieSession>> GetSessionsByStatusAsync(ProcessingStatus status)
    {
        return await _repositoryService.GetSessionsByStatusAsync(status);
    }

    /// <summary>
    /// Retrieves movie sessions where a specific participant was present.
    /// </summary>
    public async Task<List<MovieSession>> GetSessionsByParticipantAsync(string participantName)
    {
        return await _repositoryService.GetSessionsByParticipantAsync(participantName);
    }

    #endregion

    #region Session Management

    /// <summary>
    /// Deletes a movie session by its unique identifier.
    /// </summary>
    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        return await _repositoryService.DeleteSessionAsync(sessionId);
    }

    /// <summary>
    /// Updates the processing status of a movie session.
    /// </summary>
    public async Task<MovieSession> UpdateProcessingStatusAsync(string id, ProcessingStatus status, string? errorMessage = null)
    {
        return await _repositoryService.UpdateProcessingStatusAsync(id, status, errorMessage);
    }

    /// <summary>
    /// Gets the latest microphone assignments used across all sessions.
    /// </summary>
    public async Task<Dictionary<int, string>> GetLatestMicAssignmentsAsync()
    {
        return await _repositoryService.GetLatestMicAssignmentsAsync();
    }

    #endregion

    #region Maintenance Operations

    /// <summary>
    /// Fixes sessions that are stuck in analyzing status.
    /// </summary>
    public async Task<int> FixStuckAnalyzingSessionsAsync()
    {
        return await _maintenanceService.FixStuckAnalyzingSessionsAsync();
    }

    /// <summary>
    /// Fixes sessions that are stuck in processing for an extended period.
    /// </summary>
    public async Task<int> FixStuckProcessingSessionsAsync(TimeSpan stuckThreshold)
    {
        return await _maintenanceService.FixStuckProcessingSessionsAsync(stuckThreshold);
    }

    /// <summary>
    /// Attempts to recover failed audio file processing by retrying specific steps.
    /// </summary>
    public async Task<int> RecoverFailedAudioFilesAsync(string sessionId)
    {
        return await _maintenanceService.RecoverFailedAudioFilesAsync(sessionId);
    }

    /// <summary>
    /// Redownloads existing transcriptions from Gladia for sessions that have transcript IDs but missing content.
    /// </summary>
    public async Task<int> RedownloadExistingTranscriptionsAsync(string sessionId, Action<string, int, int>? progressCallback = null)
    {
        return await _maintenanceService.RedownloadExistingTranscriptionsAsync(sessionId, progressCallback);
    }

    /// <summary>
    /// Performs comprehensive diagnostics on a movie session.
    /// </summary>
    public async Task<SessionDiagnostics> DiagnoseSessionAsync(string sessionId)
    {
        return await _maintenanceService.DiagnoseSessionAsync(sessionId);
    }

    /// <summary>
    /// Performs bulk cleanup operations on failed or abandoned sessions.
    /// </summary>
    public async Task<CleanupSummary> CleanupAbandonedSessionsAsync(TimeSpan abandonedThreshold)
    {
        return await _maintenanceService.CleanupAbandonedSessionsAsync(abandonedThreshold);
    }

    /// <summary>
    /// Reruns analysis for a session that has completed transcription.
    /// </summary>
    public async Task<bool> RerunAnalysisAsync(string sessionId)
    {
        try
        {
            MovieSession? session = await _repositoryService.GetSessionAsync(sessionId);
            if (session == null)
            {
                _logger.LogWarning("Session {SessionId} not found for analysis rerun", sessionId);
                return false;
            }

            // Ensure session has transcripts
            if (!session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText)))
            {
                _logger.LogWarning("Session {SessionId} has no transcripts available for analysis", sessionId);
                return false;
            }

            _logger.LogInformation("Rerunning analysis for session {SessionId} - {MovieTitle}", sessionId, session.MovieTitle);

            // Clear existing analysis results
            session.CategoryResults = null;
            session.SessionStats = null;
            session.Status = ProcessingStatus.Analyzing;
            await _repositoryService.UpdateSessionAsync(session);

            // Run analysis
            CategoryResults analysisResults = await _analysisService.AnalyzeSessionWithFallbackAsync(session);
            session.CategoryResults = analysisResults;
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;

            // Save updated session
            await _repositoryService.UpdateSessionAsync(session);

            _logger.LogInformation("Successfully reran analysis for session {SessionId}", sessionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rerun analysis for session {SessionId}", sessionId);
            return false;
        }
    }

    #endregion

    #region Metadata Operations

    /// <summary>
    /// Extracts the session date from a folder name using various patterns.
    /// </summary>
    public DateTime ExtractDateFromFolderName(string folderName)
    {
        return _metadataService.ExtractDateFromFolderName(folderName);
    }

    /// <summary>
    /// Suggests a movie title from the folder name by cleaning and formatting.
    /// </summary>
    public string SuggestMovieTitle(string folderName)
    {
        return _metadataService.SuggestMovieTitle(folderName);
    }

    /// <summary>
    /// Determines participants based on audio files and speaker assignments.
    /// </summary>
    public void DetermineParticipants(MovieSession session)
    {
        _metadataService.DetermineParticipants(session);
    }

    #endregion
}