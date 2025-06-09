using System.Text.Json.Serialization;

namespace MovieReviewApp.Application.Models.OpenAI;

/// <summary>
/// Response model for OpenAI analysis containing categorized entertainment highlights.
/// </summary>
public class OpenAIAnalysisResponse
{
    [JsonPropertyName("MostOffensiveTake")]
    public CategoryWinnerDto? MostOffensiveTake { get; set; }

    [JsonPropertyName("HottestTake")]
    public CategoryWinnerDto? HottestTake { get; set; }

    [JsonPropertyName("BiggestArgumentStarter")]
    public CategoryWinnerDto? BiggestArgumentStarter { get; set; }

    [JsonPropertyName("BestJoke")]
    public CategoryWinnerDto? BestJoke { get; set; }

    [JsonPropertyName("BestRoast")]
    public CategoryWinnerDto? BestRoast { get; set; }

    [JsonPropertyName("FunniestRandomTangent")]
    public CategoryWinnerDto? FunniestRandomTangent { get; set; }

    [JsonPropertyName("MostPassionateDefense")]
    public CategoryWinnerDto? MostPassionateDefense { get; set; }

    [JsonPropertyName("BiggestUnanimousReaction")]
    public CategoryWinnerDto? BiggestUnanimousReaction { get; set; }

    [JsonPropertyName("MostBoringStatement")]
    public CategoryWinnerDto? MostBoringStatement { get; set; }

    [JsonPropertyName("BestPlotTwistRevelation")]
    public CategoryWinnerDto? BestPlotTwistRevelation { get; set; }

    [JsonPropertyName("MovieSnobMoment")]
    public CategoryWinnerDto? MovieSnobMoment { get; set; }

    [JsonPropertyName("GuiltyPleasureAdmission")]
    public CategoryWinnerDto? GuiltyPleasureAdmission { get; set; }

    [JsonPropertyName("QuietestPersonBestMoment")]
    public CategoryWinnerDto? QuietestPersonBestMoment { get; set; }

    [JsonPropertyName("Top5FunniestSentences")]
    public TopFiveListDto? Top5FunniestSentences { get; set; }

    [JsonPropertyName("Top5MostBlandComments")]
    public TopFiveListDto? Top5MostBlandComments { get; set; }

    [JsonPropertyName("OpeningQuestions")]
    public InitialQuestionsDto? OpeningQuestions { get; set; }
}

/// <summary>
/// Represents a winner in a specific entertainment category.
/// </summary>
public class CategoryWinnerDto
{
    [JsonPropertyName("Speaker")]
    public string Speaker { get; set; } = "Unknown";

    [JsonPropertyName("Timestamp")]
    public string Timestamp { get; set; } = "0:00";

    [JsonPropertyName("Quote")]
    public string Quote { get; set; } = "";

    [JsonPropertyName("Setup")]
    public string Setup { get; set; } = "";

    [JsonPropertyName("GroupReaction")]
    public string GroupReaction { get; set; } = "";

    [JsonPropertyName("WhyItsGreat")]
    public string WhyItsGreat { get; set; } = "";

    [JsonPropertyName("AudioQualityString")]
    public string AudioQualityString { get; set; } = "Clear";

    [JsonPropertyName("EntertainmentScore")]
    public int EntertainmentScore { get; set; } = 5;

    [JsonPropertyName("RunnersUp")]
    public List<RunnerUpDto> RunnersUp { get; set; } = new();
}

/// <summary>
/// Represents a runner-up entry in a category.
/// </summary>
public class RunnerUpDto
{
    [JsonPropertyName("Speaker")]
    public string Speaker { get; set; } = "Unknown";

    [JsonPropertyName("Timestamp")]
    public string Timestamp { get; set; } = "0:00";

    [JsonPropertyName("BriefDescription")]
    public string BriefDescription { get; set; } = "";

    [JsonPropertyName("Place")]
    public int Place { get; set; } = 2;
}

/// <summary>
/// Contains a ranked list of top five entries.
/// </summary>
public class TopFiveListDto
{
    [JsonPropertyName("Entries")]
    public List<TopFiveEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Represents a single entry in a top five list.
/// </summary>
public class TopFiveEntryDto
{
    [JsonPropertyName("Rank")]
    public int Rank { get; set; }

    [JsonPropertyName("Speaker")]
    public string Speaker { get; set; } = "Unknown";

    [JsonPropertyName("Timestamp")]
    public string Timestamp { get; set; } = "0:00";

    [JsonPropertyName("Quote")]
    public string Quote { get; set; } = "";

    [JsonPropertyName("Context")]
    public string Context { get; set; } = "";

    [JsonPropertyName("AudioQualityString")]
    public string AudioQualityString { get; set; } = "Clear";

    [JsonPropertyName("Score")]
    public double Score { get; set; } = 5.0;

    [JsonPropertyName("Reasoning")]
    public string Reasoning { get; set; } = "";

    [JsonPropertyName("EstimatedStartEnd")]
    public double[]? EstimatedStartEnd { get; set; }
}

/// <summary>
/// Contains initial questions and answers from the session.
/// </summary>
public class InitialQuestionsDto
{
    [JsonPropertyName("Questions")]
    public List<QuestionAnswerDto> Questions { get; set; } = new();
}

/// <summary>
/// Represents a question and answer pair.
/// </summary>
public class QuestionAnswerDto
{
    [JsonPropertyName("Question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("Speaker")]
    public string Speaker { get; set; } = string.Empty;

    [JsonPropertyName("Answer")]
    public string Answer { get; set; } = string.Empty;

    [JsonPropertyName("Timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("EntertainmentValue")]
    public int EntertainmentValue { get; set; } = 5;

    [JsonPropertyName("EstimatedStartEnd")]
    public double[]? EstimatedStartEnd { get; set; }
}

/// <summary>
/// OpenAI API response wrapper.
/// </summary>
public class OpenAIResponse
{
    public OpenAIChoice[]? choices { get; set; }
}

/// <summary>
/// OpenAI API choice object.
/// </summary>
public class OpenAIChoice
{
    public OpenAIMessage? message { get; set; }
}

/// <summary>
/// OpenAI API message object.
/// </summary>
public class OpenAIMessage
{
    public string? content { get; set; }
}