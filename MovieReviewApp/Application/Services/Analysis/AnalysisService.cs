using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Consolidated service for all movie session analysis operations.
/// Handles AI analysis, response parsing, and stats generation in one place.
/// </summary>
public class AnalysisService
{
    private readonly MovieSessionService _sessionService;
    private readonly OpenAIApiService _openAIApiService;
    private readonly TranscriptProcessingService _transcriptProcessingService;
    private readonly PromptGenerationService _promptGenerationService;
    private readonly ResponseParsingService _responseParsingService;
    private readonly SimpleSessionStatsService _sessionStatsService;
    private readonly GladiaService _gladiaService;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(
        MovieSessionService sessionService,
        OpenAIApiService openAIApiService,
        TranscriptProcessingService transcriptProcessingService,
        PromptGenerationService promptGenerationService,
        ResponseParsingService responseParsingService,
        SimpleSessionStatsService sessionStatsService,
        GladiaService gladiaService,
        ILogger<AnalysisService> logger)
    {
        _sessionService = sessionService;
        _openAIApiService = openAIApiService;
        _transcriptProcessingService = transcriptProcessingService;
        _promptGenerationService = promptGenerationService;
        _responseParsingService = responseParsingService;
        _sessionStatsService = sessionStatsService;
        _gladiaService = gladiaService;
        _logger = logger;
    }

    /// <summary>
    /// Processes the AI response for a session, following the state machine pattern
    /// </summary>
    public async Task<bool> ProcessAIResponseAsync(Guid sessionId)
    {
        try
        {
            _logger.LogInformation("Processing AI response for session {SessionId}", sessionId);

            MovieSession? session = await _sessionService.GetByIdAsync(sessionId);
            if (session == null)
            {
                _logger.LogError("Session {SessionId} not found", sessionId);
                return false;
            }

            // Step 1: Build enhanced transcript with speaker attribution
            _logger.LogInformation("[ANALYSIS DEBUG] Step 1: Building enhanced transcript");
            string? combinedTranscript = await _transcriptProcessingService.BuildEnhancedTranscriptForAI(session);

            _logger.LogDebug("Built combined transcript for session {SessionId}: {Length} characters",
                session.Id, combinedTranscript?.Length ?? 0);

            // Step 2: Generate analysis prompt - check for null transcript
            _logger.LogInformation("[ANALYSIS DEBUG] Step 2: Generating analysis prompt");
            if (string.IsNullOrEmpty(combinedTranscript))
            {
                _logger.LogWarning("No transcript available for session {SessionId}", sessionId);
                return false;
            }

            string prompt = _promptGenerationService.GenerateAnalysisPrompt(session, combinedTranscript);

            // Step 3: Call OpenAI API
            _logger.LogInformation("[ANALYSIS DEBUG] Step 3: Calling OpenAI API");
            string analysisResult = await _openAIApiService.AnalyzeTranscriptAsync(prompt);

            // Step 4: Parse the response
            _logger.LogInformation("[ANALYSIS DEBUG] Step 4: Parsing response");
            CategoryResults categoryResults = _responseParsingService.ParseAnalysisResponse(analysisResult);

            // Step 5: Update session with results
            session.CategoryResults = categoryResults;
            session.SessionStats = _sessionStatsService.GenerateSessionStats(session, categoryResults);

            // Step 6: Mark audio files as complete
            foreach (AudioFile file in session.AudioFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.Complete;
                file.CurrentStep = "Complete";
                file.ProgressPercentage = 100;
            }

            // Step 7: Save to database
            _ = await _sessionService.CreateAsync(session);

            _logger.LogInformation("[ANALYSIS DEBUG] Analysis completed for session {SessionId}", session.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AI response for session {SessionId}", sessionId);
            return false;
        }
    }
}