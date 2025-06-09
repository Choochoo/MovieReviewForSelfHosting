using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for generating analysis prompts for OpenAI based on movie session data.
/// Creates structured prompts that guide the AI to produce consistent analysis results.
/// </summary>
public class PromptGenerationService
{
    private readonly DiscussionQuestionsService _discussionQuestionsService;
    private readonly ILogger<PromptGenerationService> _logger;

    // Maximum transcript size to prevent OpenAI timeouts
    private const int MAX_TRANSCRIPT_SIZE = 60000;
    private const string TRUNCATION_WARNING = "\n\n[TRANSCRIPT TRUNCATED DUE TO LENGTH - ANALYSIS BASED ON FIRST {0:N0} CHARACTERS FOR OPTIMAL PROCESSING]";

    public PromptGenerationService(
        DiscussionQuestionsService discussionQuestionsService,
        ILogger<PromptGenerationService> logger)
    {
        _discussionQuestionsService = discussionQuestionsService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a comprehensive analysis prompt for OpenAI based on session data and transcript.
    /// </summary>
    public async Task<string> CreateAnalysisPromptAsync(string movieTitle, DateTime sessionDate, List<string> participants, string transcript)
    {
        // Limit transcript size to prevent timeouts
        string processedTranscript = transcript;
        bool wasTruncated = false;

        if (transcript.Length > MAX_TRANSCRIPT_SIZE)
        {
            processedTranscript = transcript.Substring(0, MAX_TRANSCRIPT_SIZE);
            processedTranscript += string.Format(TRUNCATION_WARNING, MAX_TRANSCRIPT_SIZE);
            wasTruncated = true;
            _logger.LogWarning("Transcript truncated from {OriginalLength} to {TruncatedLength} characters", 
                transcript.Length, processedTranscript.Length);
        }

        // Get initial discussion questions for context
        List<DiscussionQuestion> questions = await _discussionQuestionsService.GetActiveQuestionsAsync();
        string questionsContext = questions.Any() 
            ? $"The group was guided by these discussion questions: {string.Join(", ", questions.Select(q => q.Question))}"
            : "This was a free-form discussion without structured questions.";

        string participantsList = participants.Any() 
            ? string.Join(", ", participants)
            : "Unknown participants";

        // Create comprehensive analysis prompt
        string prompt = $@"You are an expert entertainment analyst specializing in identifying the most entertaining and memorable moments from movie discussion recordings. 

Analyze this movie discussion transcript and identify the most entertaining moments across multiple categories. The discussion is about ""{movieTitle}"" from {sessionDate:MMMM d, yyyy}, with participants: {participantsList}.

{questionsContext}

TRANSCRIPT TO ANALYZE:
{processedTranscript}

ANALYSIS REQUIREMENTS:
Please provide a comprehensive analysis in the following JSON format. For each category, identify the single best moment that fits the criteria:

{{
  ""comedy_categories"": {{
    ""best_joke"": {{
      ""category"": ""best_joke"",
      ""speaker"": ""[Name of person who made the joke]"",
      ""timestamp"": ""[MM:SS format]"",
      ""quote"": ""[Exact words that made it funny]"",
      ""setup"": ""[What led to this moment]"",
      ""group_reaction"": ""[How others responded]"",
      ""why_its_great"": ""[What makes it entertaining]"",
      ""audio_quality"": ""[clear/muffled/distorted/background_noise]"",
      ""entertainment_score"": [1-10 numeric rating]
    }},
    ""most_unintentionally_funny"": {{
      ""category"": ""most_unintentionally_funny"",
      ""speaker"": ""[Name]"",
      ""timestamp"": ""[MM:SS]"",
      ""quote"": ""[What they said]"",
      ""setup"": ""[Context]"",
      ""group_reaction"": ""[Others' response]"",
      ""why_its_great"": ""[Why it was funny despite not intending to be]"",
      ""audio_quality"": ""[clear/muffled/distorted/background_noise]"",
      ""entertainment_score"": [1-10]
    }}
  }},
  ""opinion_categories"": {{
    ""hottest_take"": {{
      ""category"": ""hottest_take"",
      ""speaker"": ""[Name]"",
      ""timestamp"": ""[MM:SS]"",
      ""quote"": ""[The controversial opinion]"",
      ""setup"": ""[What prompted this take]"",
      ""group_reaction"": ""[How others reacted]"",
      ""why_its_great"": ""[What makes it a hot take]"",
      ""audio_quality"": ""[clear/muffled/distorted/background_noise]"",
      ""entertainment_score"": [1-10]
    }}
  }},
  ""insight_categories"": {{
    ""best_movie_insight"": {{
      ""category"": ""best_movie_insight"",
      ""speaker"": ""[Name]"",
      ""timestamp"": ""[MM:SS]"",
      ""quote"": ""[The insightful comment]"",
      ""setup"": ""[Context of the insight]"",
      ""group_reaction"": ""[Others' response]"",
      ""why_its_great"": ""[What makes it insightful]"",
      ""audio_quality"": ""[clear/muffled/distorted/background_noise]"",
      ""entertainment_score"": [1-10]
    }}
  }},
  ""discussion_categories"": {{
    ""best_argument_moment"": {{
      ""category"": ""best_argument_moment"",
      ""speaker"": ""[Primary speaker in the argument]"",
      ""timestamp"": ""[MM:SS]"",
      ""quote"": ""[Key part of the argument]"",
      ""setup"": ""[What started the disagreement]"",
      ""group_reaction"": ""[How the argument developed]"",
      ""why_its_great"": ""[What made it entertaining]"",
      ""audio_quality"": ""[clear/muffled/distorted/background_noise]"",
      ""entertainment_score"": [1-10]
    }}
  }},
  ""top_5_lists"": {{
    ""funniest_sentences"": {{
      ""title"": ""Top 5 Funniest Sentences"",
      ""entries"": [
        {{
          ""speaker"": ""[Name]"",
          ""timestamp"": ""[MM:SS]"",
          ""quote"": ""[Funny sentence]"",
          ""setup"": ""[Context]"",
          ""group_reaction"": ""[Response]"",
          ""why_its_great"": ""[Why it's funny]"",
          ""audio_quality"": ""[clear/muffled/distorted/background_noise]"",
          ""entertainment_score"": [1-10],
          ""source_audio_file"": ""[filename if identifiable]""
        }}
      ]
    }},
    ""most_bland_comments"": {{
      ""title"": ""Top 5 Most Bland Comments"",
      ""entries"": [
        {{
          ""speaker"": ""[Name]"",
          ""timestamp"": ""[MM:SS]"",
          ""quote"": ""[Bland comment]"",
          ""setup"": ""[Context]"",
          ""group_reaction"": ""[Lack of response]"",
          ""why_its_great"": ""[Why it was memorably boring]"",
          ""audio_quality"": ""[clear/muffled/distorted/background_noise]"",
          ""entertainment_score"": [1-10],
          ""source_audio_file"": ""[filename if identifiable]""
        }}
      ]
    }}
  }}
}}

ANALYSIS GUIDELINES:
1. Focus on entertainment value - what would make people laugh or be engaged when reviewing this later
2. Include exact quotes when possible, but ensure they're family-friendly for sharing
3. Look for moments of genuine surprise, laughter, disagreement, or insight
4. Consider both intentional humor and unintentionally funny moments
5. Rate entertainment value from 1-10 (10 = absolutely hilarious/memorable)
6. For Top 5 lists, provide exactly 5 entries ranked by entertainment value
7. Timestamps should be in MM:SS format (estimate if exact time unclear)
8. Audio quality assessment helps determine clip generation feasibility

IMPORTANT: 
- Respond ONLY with the JSON structure above
- Do not include any explanatory text before or after the JSON
- Ensure all required fields are present for each category
- If a category has no good examples, create the structure but note in the quote that no suitable moment was found
- Focus on moments that would be entertaining when shared as audio clips

{(wasTruncated ? "\nNOTE: Transcript was truncated due to length. Analysis is based on the first portion of the discussion." : "")}";

        _logger.LogDebug("Generated analysis prompt: {PromptLength} characters", prompt.Length);
        return prompt;
    }

    /// <summary>
    /// Creates a fallback prompt for when the full analysis fails.
    /// </summary>
    public string CreateFallbackPrompt(string movieTitle, List<string> participants, string transcript)
    {
        string participantsList = participants.Any() 
            ? string.Join(", ", participants)
            : "Unknown participants";

        return $@"Analyze this movie discussion about ""{movieTitle}"" with participants: {participantsList}.

Find the most entertaining moment and provide it in this simple JSON format:

{{
  ""best_moment"": {{
    ""speaker"": ""[Name]"",
    ""timestamp"": ""[MM:SS]"",
    ""quote"": ""[What they said]"",
    ""why_its_great"": ""[Why it was entertaining]"",
    ""entertainment_score"": [1-10]
  }}
}}

TRANSCRIPT:
{transcript.Substring(0, Math.Min(transcript.Length, 10000))}

Respond only with JSON.";
    }
}