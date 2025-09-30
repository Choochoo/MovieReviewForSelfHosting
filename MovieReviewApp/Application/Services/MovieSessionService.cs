using MongoDB.Driver;
using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing movie sessions and their lifecycle.
/// Handles CRUD operations, session state management, and processing coordination.
/// </summary>
public class MovieSessionService(IRepository<MovieSession> repository, ILogger<MovieSessionService> logger)
    : BaseService<MovieSession>(repository, logger)
{

    /// <summary>
    /// Gets latest microphone assignments from most recent session.
    /// </summary>
    public async Task<Dictionary<int, string>> GetLatestMicAssignments()
    {
        List<MovieSession> recentSessions = await GetAllAsync();
        return recentSessions.FirstOrDefault()?.MicAssignments ?? new Dictionary<int, string>();
    }

    /// <summary>
    /// Gets participant name from microphone assignments.
    /// </summary>
    public string GetParticipantName(Dictionary<int, string> micAssignments, int micNumber)
    {
        return micAssignments.TryGetValue(micNumber, out string? name) ? name : $"Speaker {micNumber + 1}";
    }

    /// <summary>
    /// Retrieves movie sessions by processing status.
    /// </summary>
    public async Task<List<MovieSession>> GetSessionsByStatusAsync(ProcessingStatus status)
    {
        return await _repository.FindAsync(s => s.Status == status);
    }
}
