using MovieReviewApp.Application.Services.Analysis;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Legacy wrapper service for movie session analysis. Delegates to the new orchestrator service.
/// Use MovieSessionAnalysisOrchestrator directly for new code.
/// </summary>
public class MovieSessionAnalysisService
{
    private readonly MovieSessionAnalysisOrchestrator _orchestrator;
    private readonly ILogger<MovieSessionAnalysisService> _logger;

    public MovieSessionAnalysisService(
        MovieSessionAnalysisOrchestrator orchestrator,
        ILogger<MovieSessionAnalysisService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether the service is properly configured with an OpenAI API key.
    /// </summary>
    public bool IsConfigured => _orchestrator.IsConfigured;

    /// <summary>
    /// Analyzes a single movie session using OpenAI to extract entertainment highlights and moments.
    /// </summary>
    /// <param name="session">The movie session to analyze.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the analysis results with categorized highlights.</returns>
    public async Task<CategoryResults> AnalyzeSessionAsync(MovieSession session)
    {
        return await _orchestrator.AnalyzeSessionAsync(session);
    }

    /// <summary>
    /// Analyzes multiple movie sessions in parallel using OpenAI, with concurrency limits to respect API rate limits.
    /// </summary>
    /// <param name="sessions">The movie sessions to analyze.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of tuples with each session and its analysis results.</returns>
    public async Task<List<(MovieSession session, CategoryResults categoryResults)>> AnalyzeSessionsAsync(IEnumerable<MovieSession> sessions)
    {
        return await _orchestrator.AnalyzeSessionsAsync(sessions);
    }

    /// <summary>
    /// Generates comprehensive statistics for a movie session including participant activity, audio quality metrics, and entertainment scores.
    /// </summary>
    /// <param name="session">The movie session to generate stats for.</param>
    /// <param name="categoryResults">The analysis results containing entertainment highlights.</param>
    /// <returns>Detailed session statistics including speaking time, highlight counts, and audio quality metrics.</returns>
    public SessionStats GenerateSessionStats(MovieSession session, CategoryResults categoryResults)
    {
        return _orchestrator.GenerateSessionStats(session, categoryResults);
    }
}