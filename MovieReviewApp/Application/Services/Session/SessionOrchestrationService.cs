using MovieReviewApp.Models;

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
        return await ProcessSessionCoreAsync(
            session,
            async (sess, callback) => await _audioWorkflowService.ProcessSessionEnhancedAsync(sess, callback),
            progressCallback,
            "enhanced");
    }

    /// <summary>
    /// Processes a movie session through the standard workflow with detailed progress tracking.
    /// </summary>
    public async Task ProcessSessionStandardAsync(MovieSession session, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        await ProcessSessionCoreAsync(
            session,
            async (sess, _) =>
            {
                await _audioWorkflowService.ProcessSessionStandardAsync(sess, progressCallback);
                return sess;
            },
            (message, progress) => progressCallback?.Invoke(ProcessingStatus.Analyzing, progress, message),
            "standard",
            progressCallback);
    }

    /// <summary>
    /// Core processing logic shared between standard and enhanced workflows.
    /// </summary>
    private async Task<MovieSession> ProcessSessionCoreAsync(
        MovieSession session,
        Func<MovieSession, Action<string, int>?, Task<MovieSession>> audioProcessor,
        Action<string, int>? progressCallback,
        string workflowType,
        Action<ProcessingStatus, int, string>? detailedProgressCallback = null)
    {
        try
        {
            _logger.LogInformation("Starting {WorkflowType} processing for session {SessionId} - {MovieTitle}",
                workflowType, session.Id, session.MovieTitle);

            // Step 1: Validation
            await UpdateSessionStatusAsync(session, ProcessingStatus.Validating);
            detailedProgressCallback?.Invoke(ProcessingStatus.Validating, 10, "Validating session data");

            ValidateSessionData(session);

            // Step 2: Audio Processing
            detailedProgressCallback?.Invoke(ProcessingStatus.Transcribing, 20, "Starting audio processing");
            await UpdateSessionStatusAsync(session, ProcessingStatus.Transcribing);

            session = await audioProcessor(session, progressCallback);
            await _repositoryService.UpdateSessionAsync(session);

            // Step 3: AI Analysis
            detailedProgressCallback?.Invoke(ProcessingStatus.Analyzing, 75, "Analyzing transcripts for entertainment moments");
            await RunAIAnalysisAsync(session, progressCallback);

            // Step 4: Final Validation
            ValidateProcessingResults(session);

            // Step 5: Mark Complete
            progressCallback?.Invoke("Processing complete", 100);
            detailedProgressCallback?.Invoke(ProcessingStatus.Complete, 100, "Processing complete");

            await MarkSessionCompleteAsync(session);

            _logger.LogInformation("Successfully completed {WorkflowType} processing for session {SessionId}",
                workflowType, session.Id);

            return session;
        }
        catch (Exception ex)
        {
            await HandleProcessingErrorAsync(session, ex);
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
        MovieSession? session = await _repositoryService.GetSessionAsync(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for analysis rerun", sessionId);
            return false;
        }

        return await RerunAnalysisForSessionAsync(session);
    }

    /// <summary>
    /// Reruns analysis for a given session object.
    /// </summary>
    private async Task<bool> RerunAnalysisForSessionAsync(MovieSession session)
    {
        try
        {
            if (!HasTranscripts(session))
            {
                _logger.LogWarning("Session {SessionId} has no transcripts available for analysis", session.Id);
                return false;
            }

            _logger.LogInformation("Rerunning analysis for session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);

            ClearAnalysisResults(session);
            await RunAIAnalysisAsync(session);

            _logger.LogInformation("Successfully reran analysis for session {SessionId}", session.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rerun analysis for session {SessionId}", session.Id);
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

    #region Private Helper Methods

    private async Task UpdateSessionStatusAsync(MovieSession session, ProcessingStatus status)
    {
        session.Status = status;
        await _repositoryService.UpdateSessionAsync(session);
    }

    private void ValidateSessionData(MovieSession session)
    {
        if (!session.AudioFiles.Any())
        {
            throw new Exception("No audio files found in session");
        }
    }

    private void ValidateProcessingResults(MovieSession session)
    {
        int successfulTranscripts = session.AudioFiles.Count(f =>
            f.ProcessingStatus == AudioProcessingStatus.TranscriptsDownloaded &&
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

        if (session.CategoryResults == null)
        {
            throw new Exception("Session analysis not completed. Session not saved.");
        }
    }

    private async Task MarkSessionCompleteAsync(MovieSession session)
    {
        session.Status = ProcessingStatus.Complete;
        session.ProcessedAt = DateTime.UtcNow;
        await _repositoryService.UpdateSessionAsync(session);
    }

    private async Task HandleProcessingErrorAsync(MovieSession session, Exception ex)
    {
        _logger.LogError(ex, "Processing failed for session {SessionId}", session.Id);
        session.Status = ProcessingStatus.Failed;
        session.ErrorMessage = ex.Message;
        await _repositoryService.UpdateSessionAsync(session);
    }

    private bool HasTranscripts(MovieSession session)
    {
        return session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText));
    }

    private void ClearAnalysisResults(MovieSession session)
    {
        session.CategoryResults = null;
        session.SessionStats = null;
    }

    /// <summary>
    /// Runs AI analysis on the session - centralizes all analysis operations.
    /// </summary>
    private async Task RunAIAnalysisAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        // Clear existing results to ensure fresh analysis
        ClearAnalysisResults(session);

        // Update status
        session.Status = ProcessingStatus.Analyzing;
        await _repositoryService.UpdateSessionAsync(session);

        // Run the analysis
        session.CategoryResults = await _analysisService.AnalyzeSessionAsync(session, progressCallback);

        // Save the results
        await _repositoryService.UpdateSessionAsync(session);
    }

    #endregion
}
