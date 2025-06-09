using MovieReviewApp.Models;
using MovieReviewApp.Infrastructure.Services;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Orchestrator service for movie session analysis that coordinates multiple specialized services.
/// Handles the complete analysis workflow from transcript processing to audio clip generation.
/// </summary>
public class MovieSessionAnalysisOrchestrator
{
    private readonly OpenAIApiService _openAIApiService;
    private readonly TranscriptProcessingService _transcriptProcessingService;
    private readonly PromptGenerationService _promptGenerationService;
    private readonly ResponseParsingService _responseParsingService;
    private readonly SimpleSessionStatsService _sessionStatsService;
    private readonly AudioClipService _audioClipService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<MovieSessionAnalysisOrchestrator> _logger;

    public MovieSessionAnalysisOrchestrator(
        OpenAIApiService openAIApiService,
        TranscriptProcessingService transcriptProcessingService,
        PromptGenerationService promptGenerationService,
        ResponseParsingService responseParsingService,
        SimpleSessionStatsService sessionStatsService,
        AudioClipService audioClipService,
        IWebHostEnvironment environment,
        ILogger<MovieSessionAnalysisOrchestrator> logger)
    {
        _openAIApiService = openAIApiService;
        _transcriptProcessingService = transcriptProcessingService;
        _promptGenerationService = promptGenerationService;
        _responseParsingService = responseParsingService;
        _sessionStatsService = sessionStatsService;
        _audioClipService = audioClipService;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether the analysis service is properly configured.
    /// </summary>
    public bool IsConfigured => _openAIApiService.IsConfigured;

    /// <summary>
    /// Analyzes a single movie session using OpenAI to extract entertainment highlights and moments.
    /// </summary>
    public async Task<CategoryResults> AnalyzeSessionAsync(MovieSession session)
    {
        List<(MovieSession session, CategoryResults categoryResults)> results = await AnalyzeSessionsAsync(new[] { session });
        return results.First().categoryResults;
    }

    /// <summary>
    /// Analyzes multiple movie sessions in parallel using OpenAI, with concurrency limits to respect API rate limits.
    /// </summary>
    public async Task<List<(MovieSession session, CategoryResults categoryResults)>> AnalyzeSessionsAsync(IEnumerable<MovieSession> sessions)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("OpenAI API key not configured - cannot perform analysis");
        }

        List<MovieSession> sessionList = sessions.ToList();
        _logger.LogInformation("Starting parallel analysis of {SessionCount} sessions", sessionList.Count);

        // Configure parallelism - adjust based on your OpenAI rate limits
        int maxConcurrency = Math.Min(sessionList.Count, 3); // Start with 3 concurrent requests
        SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        List<(MovieSession session, CategoryResults categoryResults)> analysisResults = new List<(MovieSession session, CategoryResults categoryResults)>();
        IEnumerable<Task<(MovieSession, CategoryResults)>> tasks = sessionList.Select(async session =>
        {
            await semaphore.WaitAsync();
            try
            {
                _logger.LogInformation("Starting analysis for session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);

                CategoryResults categoryResults = await AnalyzeSingleSessionAsync(session);

                lock (analysisResults)
                {
                    analysisResults.Add((session, categoryResults));
                }

                _logger.LogInformation("Completed analysis for session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);
                return (session, categoryResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);

                // Return empty results for failed sessions rather than failing the entire batch
                CategoryResults emptyResults = new CategoryResults();
                lock (analysisResults)
                {
                    analysisResults.Add((session, emptyResults));
                }
                return (session, emptyResults);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("Completed parallel analysis of {SessionCount} sessions", sessionList.Count);
        return analysisResults.OrderBy(r => sessionList.IndexOf(r.session)).ToList();
    }

    /// <summary>
    /// Analyzes a single session through the complete pipeline.
    /// </summary>
    private async Task<CategoryResults> AnalyzeSingleSessionAsync(MovieSession session)
    {
        // Step 1: Build enhanced transcript with speaker attribution (runs BEFORE OpenAI analysis)
        string? combinedTranscript = await _transcriptProcessingService.BuildEnhancedTranscriptForAI(session);

        _logger.LogDebug("Built combined transcript for session {SessionId}: {Length} characters",
            session.Id, combinedTranscript?.Length ?? 0);

        if (string.IsNullOrEmpty(combinedTranscript))
        {
            List<string> fileInfo = session.AudioFiles.Select(f => 
                $"{f.FileName}: HasTranscript={!string.IsNullOrEmpty(f.TranscriptText)}, Status={f.ProcessingStatus}").ToList();
            _logger.LogWarning("No transcript content available for analysis. Audio files: {FileInfo}", string.Join(", ", fileInfo));
            throw new Exception("No transcript content available for analysis");
        }

        if (combinedTranscript.Length < 500)
        {
            _logger.LogWarning("Combined transcript is very short ({Length} chars) - OpenAI may not have enough content to analyze", combinedTranscript.Length);
        }

        // Step 2: Create analysis prompt
        string analysisPrompt = await _promptGenerationService.CreateAnalysisPromptAsync(
            session.MovieTitle, 
            session.Date, 
            session.ParticipantsPresent, 
            combinedTranscript);

        _logger.LogDebug("Generated analysis prompt: {PromptSize:N0} characters, {TranscriptSize:N0} transcript chars",
            analysisPrompt.Length, combinedTranscript.Length);

        // Step 3: Call OpenAI for analysis
        string analysisResult = await _openAIApiService.CallOpenAIForAnalysisWithRetry(analysisPrompt);

        // Step 4: Save OpenAI response to session folder
        await SaveOpenAIResponseAsync(session, analysisPrompt, analysisResult);

        // Step 5: Parse the AI response into CategoryResults
        CategoryResults categoryResults = _responseParsingService.ParseAnalysisResult(analysisResult);

        // Step 6: Generate audio clips for highlights
        await GenerateAudioClipsAsync(session, categoryResults);

        return categoryResults;
    }

    /// <summary>
    /// Generates comprehensive statistics for a movie session.
    /// </summary>
    public SessionStats GenerateSessionStats(MovieSession session, CategoryResults categoryResults)
    {
        return _sessionStatsService.GenerateSessionStats(session, categoryResults);
    }

    /// <summary>
    /// Saves the OpenAI analysis prompt and response to the session's output folder for debugging.
    /// </summary>
    private async Task SaveOpenAIResponseAsync(MovieSession session, string prompt, string response)
    {
        try
        {
            string sessionOutputPath = GetSessionOutputPath(session);
            Directory.CreateDirectory(sessionOutputPath);

            // Save prompt
            string promptPath = Path.Combine(sessionOutputPath, "analysis_prompt.txt");
            await File.WriteAllTextAsync(promptPath, prompt);

            // Save response
            string responsePath = Path.Combine(sessionOutputPath, "openai_response.json");
            await File.WriteAllTextAsync(responsePath, response);

            _logger.LogDebug("Saved OpenAI analysis files to {OutputPath}", sessionOutputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save OpenAI response files for session {SessionId}", session.Id);
        }
    }

    /// <summary>
    /// Generates audio clips for the entertainment highlights found in the analysis.
    /// </summary>
    private async Task GenerateAudioClipsAsync(MovieSession session, CategoryResults categoryResults)
    {
        try
        {
            _logger.LogInformation("Generating audio clips for session {SessionId}", session.Id);

            // Find the master recording for clip generation
            AudioFile? masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording);
            if (masterFile == null)
            {
                _logger.LogWarning("No master recording found for session {SessionId}, skipping audio clip generation", session.Id);
                return;
            }

            // Generate clips for each category winner
            List<Task> clipTasks = new List<Task>();

            if (categoryResults.BestJoke != null)
                clipTasks.Add(GenerateClipForCategory(session, masterFile, categoryResults.BestJoke, "best_joke"));

            if (categoryResults.HottestTake != null)
                clipTasks.Add(GenerateClipForCategory(session, masterFile, categoryResults.HottestTake, "hottest_take"));

            if (categoryResults.BestPlotTwistRevelation != null)
                clipTasks.Add(GenerateClipForCategory(session, masterFile, categoryResults.BestPlotTwistRevelation, "best_plot_twist"));

            if (categoryResults.MostOffensiveTake != null)
                clipTasks.Add(GenerateClipForCategory(session, masterFile, categoryResults.MostOffensiveTake, "offensive_take"));

            if (categoryResults.BiggestArgumentStarter != null)
                clipTasks.Add(GenerateClipForCategory(session, masterFile, categoryResults.BiggestArgumentStarter, "argument_starter"));

            // Generate clips for top 5 lists
            if (categoryResults.FunniestSentences?.Entries.Any() == true)
            {
                for (int i = 0; i < Math.Min(3, categoryResults.FunniestSentences.Entries.Count); i++) // Only top 3
                {
                    TopFiveEntry entry = categoryResults.FunniestSentences.Entries[i];
                    CategoryWinner winner = new CategoryWinner
                    {
                        Speaker = entry.Speaker,
                        Timestamp = entry.Timestamp,
                        Quote = entry.Quote,
                        Setup = entry.Context, // TopFiveEntry uses Context instead of Setup
                        GroupReaction = "", // Not available in TopFiveEntry
                        WhyItsGreat = entry.Reasoning, // TopFiveEntry uses Reasoning instead
                        AudioQuality = entry.AudioQuality,
                        EntertainmentScore = (int)entry.Score
                    };
                    clipTasks.Add(GenerateClipForCategory(session, masterFile, winner, $"funny_sentence_{i + 1}"));
                }
            }

            await Task.WhenAll(clipTasks);
            _logger.LogInformation("Completed audio clip generation for session {SessionId}", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate audio clips for session {SessionId}", session.Id);
        }
    }

    /// <summary>
    /// Generates an audio clip for a specific category winner.
    /// </summary>
    private async Task GenerateClipForCategory(MovieSession session, AudioFile masterFile, CategoryWinner winner, string categoryName)
    {
        try
        {
            // Parse timestamp to get start time in seconds
            TimeSpan timeSpan = ParseTimestamp(winner.Timestamp);
            double startTimeSeconds = timeSpan.TotalSeconds;
            double endTimeSeconds = startTimeSeconds + 30; // 30 second clips

            await _audioClipService.GenerateAudioClipAsync(
                masterFile.FilePath,
                startTimeSeconds,
                endTimeSeconds,
                session.Id.ToString(),
                categoryName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate clip for {Category} in session {SessionId}", categoryName, session.Id);
        }
    }

    /// <summary>
    /// Parses a timestamp string (MM:SS) into a TimeSpan.
    /// </summary>
    private TimeSpan ParseTimestamp(string timestamp)
    {
        try
        {
            string[] parts = timestamp.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
            {
                return new TimeSpan(0, minutes, seconds);
            }
        }
        catch
        {
            // Fallback to 0 if parsing fails
        }
        return TimeSpan.Zero;
    }

    /// <summary>
    /// Gets the output path for storing session analysis files.
    /// </summary>
    private string GetSessionOutputPath(MovieSession session)
    {
        string uploadsPath = Path.Combine(_environment.WebRootPath, "uploads");
        string sessionFolder = $"{session.Date:MM-yyyy}";
        string sessionOutputPath = Path.Combine(uploadsPath, sessionFolder, session.Id.ToString(), "analysis");
        return sessionOutputPath;
    }
}