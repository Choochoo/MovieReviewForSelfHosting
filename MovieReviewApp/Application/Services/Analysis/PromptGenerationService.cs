using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for generating analysis prompts for OpenAI based on movie session data.
/// Creates structured prompts that guide the AI to produce consistent analysis results.
/// </summary>
public class PromptGenerationService
{
    private readonly DiscussionQuestionService _discussionQuestionsService;
    private readonly ILogger<PromptGenerationService> _logger;

    // Maximum transcript size to prevent OpenAI timeouts  
    // GPT-4 can handle ~128K tokens (500K+ characters), so increased significantly
    private const int MAX_TRANSCRIPT_SIZE = 400000;
    private const string TRUNCATION_WARNING = "\n\n[TRANSCRIPT TRUNCATED DUE TO LENGTH - ANALYSIS BASED ON FIRST {0:N0} CHARACTERS FOR OPTIMAL PROCESSING]";

    public PromptGenerationService(
        DiscussionQuestionService discussionQuestionsService,
        ILogger<PromptGenerationService> logger)
    {
        _discussionQuestionsService = discussionQuestionsService;
        _logger = logger;
    }

    /// <summary>
    /// Generates analysis prompt for a session and transcript.
    /// </summary>
    public string GenerateAnalysisPrompt(MovieSession session, string transcript)
    {
        return CreateAnalysisPromptAsync(session.MovieTitle, session.Date, session.Participants, transcript).Result;
    }

    /// <summary>
    /// Creates a comprehensive analysis prompt for OpenAI based on session data and transcript.
    /// </summary>
    public async Task<string> CreateAnalysisPromptAsync(string movieTitle, DateTime sessionDate, IEnumerable<string> participants, string transcript)
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

        // Build dynamic opening questions for each participant using database questions
        string openingQuestionsJson = BuildOpeningQuestionsJson(participants, questions);

        // Create comprehensive analysis prompt
        string prompt = $@"You are an expert entertainment analyst specializing in identifying the most entertaining and memorable moments from movie discussion recordings. 

Analyze this movie discussion transcript and identify the most entertaining moments across multiple categories. The discussion is about ""{movieTitle}"" from {sessionDate:MMMM d, yyyy}, with participants: {participantsList}.

{questionsContext}

TRANSCRIPT TO ANALYZE:
{processedTranscript}

ANALYSIS REQUIREMENTS:
Please provide a comprehensive analysis in the following JSON format. For each category, identify the single best moment that fits the criteria:

{{
  ""AIsUniqueObservations"": {{
    ""Entries"": [
      {{
        ""Rank"": 1,
        ""Speaker"": ""[Name or 'Multiple' if it involves the group]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[The most unique/interesting moment you observed]"",
        ""Context"": ""[Context of what made this unique]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[What made this observation particularly unique, unexpected, or entertaining from your AI perspective]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 2,
        ""Speaker"": ""[Name or 'Multiple' if it involves the group]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Second most unique/interesting moment you observed]"",
        ""Context"": ""[Context of what made this unique]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[What made this observation unique from your AI perspective]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 3,
        ""Speaker"": ""[Name or 'Multiple' if it involves the group]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Third most unique/interesting moment you observed]"",
        ""Context"": ""[Context of what made this unique]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[What made this observation unique from your AI perspective]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }}
    ]
  }},
  ""MostOffensiveTake"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The offensive/controversial statement]"",
    ""Setup"": ""[Context leading to this moment]"",
    ""GroupReaction"": ""[How others reacted]"",
    ""WhyItsGreat"": ""[What makes it entertaining/memorable]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10],
    ""RunnersUp"": [
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their moment]"",
        ""Place"": 2
      }},
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their moment]"",
        ""Place"": 3
      }}
    ]
  }},
  ""HottestTake"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The controversial opinion]"",
    ""Setup"": ""[What prompted this take]"",
    ""GroupReaction"": ""[How others reacted]"",
    ""WhyItsGreat"": ""[What makes it a hot take]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10],
    ""RunnersUp"": [
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their hot take]"",
        ""Place"": 2
      }},
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their hot take]"",
        ""Place"": 3
      }}
    ]
  }},
  ""BiggestArgumentStarter"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[Statement that started argument]"",
    ""Setup"": ""[Context before the argument]"",
    ""GroupReaction"": ""[How the argument developed]"",
    ""WhyItsGreat"": ""[What made it entertaining]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10],
    ""RunnersUp"": [
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their argument starter]"",
        ""Place"": 2
      }},
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their argument starter]"",
        ""Place"": 3
      }}
    ]
  }},
  ""BestJoke"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The joke or funny comment]"",
    ""Setup"": ""[What led to this moment]"",
    ""GroupReaction"": ""[How others responded/laughed]"",
    ""WhyItsGreat"": ""[What makes it funny]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10],
    ""RunnersUp"": [
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their joke]"",
        ""Place"": 2
      }},
      {{
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""BriefDescription"": ""[Brief description of their joke]"",
        ""Place"": 3
      }}
    ]
  }},
  ""BestRoast"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The roast/burn]"",
    ""Setup"": ""[Context of the roast]"",
    ""GroupReaction"": ""[Others' reaction to the burn]"",
    ""WhyItsGreat"": ""[What made it a good roast]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""FunniestRandomTangent"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The random tangent]"",
    ""Setup"": ""[What they were originally talking about]"",
    ""GroupReaction"": ""[How others responded to the tangent]"",
    ""WhyItsGreat"": ""[What made the tangent entertaining]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""MostPassionateDefense"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The passionate defense]"",
    ""Setup"": ""[What they were defending]"",
    ""GroupReaction"": ""[Others' reaction to the passion]"",
    ""WhyItsGreat"": ""[What made it memorable]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""BiggestUnanimousReaction"": {{
    ""Speaker"": ""[Person who caused the reaction]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[What caused everyone to react]"",
    ""Setup"": ""[Context before the moment]"",
    ""GroupReaction"": ""[The unanimous reaction from everyone]"",
    ""WhyItsGreat"": ""[Why everyone reacted the same way]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""MostBoringStatement"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The boring statement]"",
    ""Setup"": ""[Context]"",
    ""GroupReaction"": ""[Lack of reaction or boredom]"",
    ""WhyItsGreat"": ""[Why it was memorably boring]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""BestPlotTwistRevelation"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The revelation or insight]"",
    ""Setup"": ""[What led to this revelation]"",
    ""GroupReaction"": ""[Others' surprise or interest]"",
    ""WhyItsGreat"": ""[What made it a good plot twist/revelation]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""MovieSnobMoment"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The snobbish film comment]"",
    ""Setup"": ""[Context of the snobbery]"",
    ""GroupReaction"": ""[Others' reaction to the snobbishness]"",
    ""WhyItsGreat"": ""[What made it peak movie snob behavior]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""GuiltyPleasureAdmission"": {{
    ""Speaker"": ""[Name]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[The guilty pleasure admission]"",
    ""Setup"": ""[What led to this admission]"",
    ""GroupReaction"": ""[Others' reaction to the confession]"",
    ""WhyItsGreat"": ""[What made it a good guilty pleasure moment]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""QuietestPersonBestMoment"": {{
    ""Speaker"": ""[Name of quietest person]"",
    ""Timestamp"": ""[MM:SS]"",
    ""Quote"": ""[Their best contribution]"",
    ""Setup"": ""[Context of when they spoke up]"",
    ""GroupReaction"": ""[Others' reaction to them speaking]"",
    ""WhyItsGreat"": ""[What made this their standout moment]"",
    ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
    ""EntertainmentScore"": [1-10]
  }},
  ""Top5FunniestSentences"": {{
    ""Entries"": [
      {{
        ""Rank"": 1,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Funny sentence]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it's funny]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 2,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Funny sentence]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it's funny]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 3,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Funny sentence]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it's funny]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 4,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Funny sentence]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it's funny]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 5,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Funny sentence]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it's funny]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }}
    ]
  }},
  ""Top5MostBlandComments"": {{
    ""Entries"": [
      {{
        ""Rank"": 1,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Bland comment]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it was bland/boring]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 2,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Bland comment]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it was bland/boring]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 3,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Bland comment]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it was bland/boring]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 4,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Bland comment]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it was bland/boring]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }},
      {{
        ""Rank"": 5,
        ""Speaker"": ""[Name]"",
        ""Timestamp"": ""[MM:SS]"",
        ""Quote"": ""[Bland comment]"",
        ""Context"": ""[Setup/context]"",
        ""AudioQualityString"": ""[Clear/Muffled/Distorted/Background_Noise]"",
        ""Score"": [1-10.0],
        ""Reasoning"": ""[Why it was bland/boring]"",
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }}
    ]
  }},
  {openingQuestionsJson}
}}

ANALYSIS GUIDELINES:
1. AI's Unique Observations: This is YOUR perspective as an AI - identify the 3 most unique, unexpected, or fascinating things about this conversation. These could be unusual dynamics, interesting patterns you noticed, unexpected insights, unique group behavior, conversation quirks, or anything that caught your artificial attention as distinctive about this particular discussion. Think beyond the structured categories below.
2. Focus on entertainment value - what would make people laugh or be engaged when reviewing this later
3. Include exact quotes when possible, the most non politically correct the better.
4. Look for moments of genuine surprise, laughter, disagreement, or insight
5. Consider both intentional humor and unintentionally funny moments
6. Rate entertainment value from 1-10 (10 = absolutely hilarious/memorable)
7. For Top 5 lists, provide exactly 5 entries ranked by entertainment value
8. Timestamps should be in MM:SS format (estimate if exact time unclear)
9. Audio quality assessment helps determine clip generation feasibility

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
    /// Builds dynamic opening questions JSON for each participant using database questions.
    /// </summary>
    private string BuildOpeningQuestionsJson(IEnumerable<string> participants, IEnumerable<DiscussionQuestion> dbQuestions)
    {
        if (!participants.Any() || !dbQuestions.Any())
        {
            return @"""OpeningQuestions"": {
    ""Questions"": []
  }";
        }

        List<string> questionEntries = new List<string>();

        // Limit to 2-3 participants to avoid response length issues
        List<string> selectedParticipants = participants.Take(3).ToList();

        // Create entries for selected participants answering each question from the database
        foreach (string participant in selectedParticipants)
        {
            foreach (DiscussionQuestion dbQuestion in dbQuestions.Take(3)) // Limit to 3 questions max
            {
                string entry = $@"      {{
        ""Question"": ""{dbQuestion.Question.Replace("\"", "\\\"")}"",
        ""Speaker"": ""{participant}"",
        ""Answer"": ""[Their response]"",
        ""Timestamp"": ""[MM:SS]"",
        ""EntertainmentValue"": [1-10],
        ""EstimatedStartEnd"": [start_seconds, end_seconds]
      }}";
                questionEntries.Add(entry);
            }
        }

        string questionsArray = string.Join(",\n", questionEntries);

        return $@"""OpeningQuestions"": {{
    ""Questions"": [
{questionsArray}
    ]
  }}";
    }

}
