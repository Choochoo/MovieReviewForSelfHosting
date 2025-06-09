using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;
using System.Linq.Expressions;

namespace MovieReviewApp.Application.Services.Session;

/// <summary>
/// Service responsible for database operations related to movie sessions.
/// Handles CRUD operations, queries, and data persistence for movie sessions.
/// </summary>
public class SessionRepositoryService
{
    private readonly IDatabaseService _database;
    private readonly ILogger<SessionRepositoryService> _logger;

    public SessionRepositoryService(IDatabaseService database, ILogger<SessionRepositoryService> logger)
    {
        _database = database;
        _logger = logger;
    }

    #region Query Methods

    /// <summary>
    /// Retrieves all movie sessions from the database.
    /// </summary>
    public async Task<List<MovieSession>> GetAllSessionsAsync()
    {
        return await _database.GetAllAsync<MovieSession>();
    }

    /// <summary>
    /// Retrieves the most recent movie sessions, ordered by date descending.
    /// </summary>
    public async Task<List<MovieSession>> GetRecentSessionsAsync(int limit = 10)
    {
        List<MovieSession> allSessions = await _database.GetAllAsync<MovieSession>();
        return allSessions
            .OrderByDescending(s => s.Date)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Retrieves a specific movie session by its unique identifier.
    /// </summary>
    public async Task<MovieSession?> GetSessionAsync(string sessionId)
    {
        return await _database.GetByIdAsync<MovieSession>(sessionId);
    }

    /// <summary>
    /// Searches for movie sessions based on a search term matching movie title or participants.
    /// </summary>
    public async Task<List<MovieSession>> SearchSessionsAsync(string searchTerm)
    {
        List<MovieSession> allSessions = await _database.GetAllAsync<MovieSession>();
        return allSessions.Where(s => 
            s.MovieTitle.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            s.ParticipantsPresent.Any(p => p.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    /// <summary>
    /// Gets the total count of movie sessions in the database.
    /// </summary>
    public async Task<long> GetSessionCountAsync()
    {
        return await _database.CountAsync<MovieSession>();
    }

    /// <summary>
    /// Determines if there are any movie sessions in the database.
    /// </summary>
    public async Task<bool> HasAnySessionsAsync()
    {
        return await GetSessionCountAsync() > 0;
    }

    /// <summary>
    /// Retrieves movie sessions within a specific date range.
    /// </summary>
    public async Task<List<MovieSession>> GetSessionsByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _database.FindAsync<MovieSession>(s => s.Date >= start && s.Date <= end);
    }

    /// <summary>
    /// Retrieves all movie sessions that have failed processing.
    /// </summary>
    public async Task<List<MovieSession>> GetFailedSessionsAsync()
    {
        return await _database.FindAsync<MovieSession>(s => s.Status == ProcessingStatus.Failed);
    }

    /// <summary>
    /// Retrieves movie sessions by processing status.
    /// </summary>
    public async Task<List<MovieSession>> GetSessionsByStatusAsync(ProcessingStatus status)
    {
        return await _database.FindAsync<MovieSession>(s => s.Status == status);
    }

    /// <summary>
    /// Retrieves movie sessions where a specific participant was present.
    /// </summary>
    public async Task<List<MovieSession>> GetSessionsByParticipantAsync(string participantName)
    {
        return await _database.FindAsync<MovieSession>(s => s.ParticipantsPresent.Contains(participantName));
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Creates a new movie session in the database.
    /// </summary>
    public async Task<MovieSession> CreateSessionAsync(MovieSession session)
    {
        session.CreatedAt = DateTime.UtcNow;
        await _database.InsertAsync(session);
        return session;
    }

    /// <summary>
    /// Updates an existing movie session in the database.
    /// </summary>
    public async Task<MovieSession> UpdateSessionAsync(MovieSession session)
    {
        await _database.UpsertAsync(session);
        return session;
    }

    /// <summary>
    /// Deletes a movie session by its unique identifier.
    /// </summary>
    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        return await _database.DeleteByIdAsync<MovieSession>(Guid.Parse(sessionId));
    }

    /// <summary>
    /// Updates the processing status of a movie session.
    /// </summary>
    public async Task<MovieSession> UpdateProcessingStatusAsync(string id, ProcessingStatus status, string? errorMessage = null)
    {
        MovieSession? session = await GetSessionAsync(id);
        if (session == null)
        {
            throw new ArgumentException($"Session with ID {id} not found", nameof(id));
        }

        session.Status = status;

        if (status == ProcessingStatus.Failed && !string.IsNullOrEmpty(errorMessage))
        {
            session.ErrorMessage = errorMessage;
        }
        else if (status == ProcessingStatus.Complete)
        {
            session.ProcessedAt = DateTime.UtcNow;
        }

        return await UpdateSessionAsync(session);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the latest microphone assignments used across all sessions.
    /// </summary>
    public async Task<Dictionary<int, string>> GetLatestMicAssignmentsAsync()
    {
        List<MovieSession> recentSessions = await GetRecentSessionsAsync(10);
        
        // Find the most recent session with mic assignments
        MovieSession? sessionWithAssignments = recentSessions
            .Where(s => s.MicAssignments?.Any() == true)
            .FirstOrDefault();

        return sessionWithAssignments?.MicAssignments ?? new Dictionary<int, string>();
    }

    /// <summary>
    /// Finds sessions that may be stuck in analyzing status and returns count of fixed sessions.
    /// </summary>
    public async Task<int> FixStuckAnalyzingSessionsAsync()
    {
        // Find sessions stuck in "Analyzing" status for more than 30 minutes
        DateTime cutoffTime = DateTime.UtcNow.AddMinutes(-30);
        List<MovieSession> stuckSessions = await _database.FindAsync<MovieSession>(s => 
            s.Status == ProcessingStatus.Analyzing && 
            s.CreatedAt < cutoffTime);

        int fixedCount = 0;
        foreach (MovieSession session in stuckSessions)
        {
            try
            {
                _logger.LogInformation("Fixing stuck session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);
                
                // Reset to a state where it can be reprocessed
                session.Status = ProcessingStatus.Transcribing;
                session.ErrorMessage = "Session was stuck in analyzing status and has been reset";
                
                await UpdateSessionAsync(session);
                fixedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fix stuck session {SessionId}", session.Id);
            }
        }

        if (fixedCount > 0)
        {
            _logger.LogInformation("Fixed {FixedCount} stuck sessions", fixedCount);
        }

        return fixedCount;
    }

    #endregion

    #region Bulk Operations

    /// <summary>
    /// Saves multiple sessions in a batch operation.
    /// </summary>
    public async Task<List<MovieSession>> SaveSessionsBatchAsync(IEnumerable<MovieSession> sessions)
    {
        List<MovieSession> savedSessions = new List<MovieSession>();
        
        foreach (MovieSession session in sessions)
        {
            MovieSession savedSession = await UpdateSessionAsync(session);
            savedSessions.Add(savedSession);
        }
        
        return savedSessions;
    }

    /// <summary>
    /// Retrieves sessions using a custom filter expression.
    /// </summary>
    public async Task<List<MovieSession>> FindSessionsAsync(Expression<Func<MovieSession, bool>> filter)
    {
        return await _database.FindAsync(filter);
    }

    #endregion
}