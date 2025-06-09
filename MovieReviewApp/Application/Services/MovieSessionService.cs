using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Application.Services.Session;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Legacy wrapper service for backward compatibility. Delegates to the new orchestration service.
/// Use SessionOrchestrationService directly for new code.
/// </summary>
public class MovieSessionService
{
    private readonly SessionOrchestrationService _orchestrationService;
    private readonly ILogger<MovieSessionService> _logger;

    public MovieSessionService(
        SessionOrchestrationService orchestrationService,
        ILogger<MovieSessionService> logger)
    {
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    #region Session Creation and Management

    /// <summary>
    /// Prepares a movie session from a folder containing audio files, extracting metadata and scanning for audio content.
    /// </summary>
    public async Task<MovieSession> PrepareSessionFromFolder(string folderPath, Dictionary<int, string>? micAssignments = null)
    {
        return await _orchestrationService.PrepareSessionFromFolderAsync(folderPath, micAssignments);
    }

    /// <summary>
    /// Saves a movie session to the database with a generated unique identifier.
    /// </summary>
    public async Task<MovieSession> SaveSessionToDatabase(MovieSession session)
    {
        return await _orchestrationService.SaveSessionAsync(session);
    }

    /// <summary>
    /// Processes a movie session with enhanced features including transcription, analysis, and audio clip generation.
    /// </summary>
    public async Task<MovieSession> ProcessSessionEnhanced(MovieSession session, Action<string, int>? progressCallback = null)
    {
        return await _orchestrationService.ProcessSessionEnhancedAsync(session, progressCallback);
    }

    /// <summary>
    /// Processes a movie session through the complete pipeline: validation, transcription, and analysis.
    /// </summary>
    public async Task ProcessSession(MovieSession session, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        await _orchestrationService.ProcessSessionStandardAsync(session, progressCallback);
    }

    /// <summary>
    /// Processes a movie session by ID through the complete pipeline: validation, transcription, and analysis.
    /// </summary>
    public async Task ProcessSession(string sessionId, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        await _orchestrationService.ProcessSessionByIdAsync(sessionId, progressCallback);
    }

    #endregion

    #region Session Queries

    /// <summary>
    /// Retrieves all movie sessions from the database.
    /// </summary>
    public async Task<List<MovieSession>> GetAllSessions()
    {
        return await _orchestrationService.GetAllSessionsAsync();
    }

    /// <summary>
    /// Retrieves the most recent movie sessions, ordered by date descending.
    /// </summary>
    public async Task<List<MovieSession>> GetRecentSessions(int limit = 10)
    {
        return await _orchestrationService.GetRecentSessionsAsync(limit);
    }

    public async Task<MovieSession?> GetSession(string sessionId)
    {
        return await _orchestrationService.GetSessionAsync(sessionId);
    }

    public async Task<bool> DeleteSession(string sessionId)
    {
        return await _orchestrationService.DeleteSessionAsync(sessionId);
    }

    public async Task<List<MovieSession>> SearchSessions(string searchTerm)
    {
        return await _orchestrationService.SearchSessionsAsync(searchTerm);
    }

    public async Task<long> GetSessionCount()
    {
        return await _orchestrationService.GetSessionCountAsync();
    }

    public async Task<bool> HasAnySessions()
    {
        return await _orchestrationService.HasAnySessionsAsync();
    }

    public async Task<List<MovieSession>> GetSessionsByDateRange(DateTime start, DateTime end)
    {
        return await _orchestrationService.GetSessionsByDateRangeAsync(start, end);
    }

    public async Task<List<MovieSession>> GetFailedSessions()
    {
        return await _orchestrationService.GetFailedSessionsAsync();
    }

    public async Task<Dictionary<int, string>> GetLatestMicAssignments()
    {
        return await _orchestrationService.GetLatestMicAssignmentsAsync();
    }

    #endregion

    #region Maintenance Operations

    /// <summary>
    /// Fixes sessions that are stuck in analyzing status
    /// </summary>
    public async Task<int> FixStuckAnalyzingSessions()
    {
        return await _orchestrationService.FixStuckAnalyzingSessionsAsync();
    }

    /// <summary>
    /// Reruns the OpenAI analysis for a session that has completed transcription
    /// </summary>
    public async Task<bool> RerunAnalysis(string sessionId)
    {
        return await _orchestrationService.RerunAnalysisAsync(sessionId);
    }

    public async Task<int> RedownloadExistingTranscriptions(MovieSession session, Action<string, int, int>? progressCallback = null)
    {
        return await _orchestrationService.RedownloadExistingTranscriptionsAsync(session.Id.ToString(), progressCallback);
    }

    #endregion

    #region Legacy API Methods (for backward compatibility)

    public async Task<List<MovieSession>> GetAllAsync()
    {
        return await _orchestrationService.GetAllSessionsAsync();
    }

    public async Task<MovieSession?> GetByIdAsync(string id)
    {
        return await _orchestrationService.GetSessionAsync(id);
    }

    public async Task<List<MovieSession>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _orchestrationService.GetSessionsByDateRangeAsync(startDate, endDate);
    }

    public async Task<MovieSession> CreateAsync(MovieSession session)
    {
        return await _orchestrationService.CreateSessionAsync(session);
    }

    public async Task<MovieSession> UpdateAsync(MovieSession session)
    {
        return await _orchestrationService.SaveSessionAsync(session);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        return await _orchestrationService.DeleteSessionAsync(id);
    }

    public async Task<List<MovieSession>> GetByStatusAsync(ProcessingStatus status)
    {
        return await _orchestrationService.GetSessionsByStatusAsync(status);
    }

    public async Task<List<MovieSession>> GetByParticipantAsync(string participantName)
    {
        return await _orchestrationService.GetSessionsByParticipantAsync(participantName);
    }

    public async Task<MovieSession> UpdateProcessingStatusAsync(string id, ProcessingStatus status, string? errorMessage = null)
    {
        return await _orchestrationService.UpdateProcessingStatusAsync(id, status, errorMessage);
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Gets a standardized participant name for a given microphone number.
    /// </summary>
    public static string GetParticipantName(int micNumber)
    {
        return $"Mic {micNumber}";
    }

    #endregion
}