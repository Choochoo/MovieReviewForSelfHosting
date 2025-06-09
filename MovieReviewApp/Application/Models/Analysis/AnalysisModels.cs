namespace MovieReviewApp.Application.Models.Analysis;

/// <summary>
/// Audio quality metrics for a session.
/// </summary>
public class AudioQualityMetrics
{
    public double ClearAudioPercentage { get; set; }
    public int TotalClearFiles { get; set; }
    public int TotalFiles { get; set; }
}

/// <summary>
/// Statistics for an individual participant in a session.
/// </summary>
public class ParticipantStat
{
    public string Name { get; set; } = string.Empty;
    public int TotalHighlights { get; set; }
    public Dictionary<string, int> CategoryWins { get; set; } = new();
    public double EntertainmentScoreAverage { get; set; }
    public double SpeakingTimePercentage { get; set; }
}

/// <summary>
/// Initial questions and answers from the beginning of the session.
/// </summary>
public class InitialQuestions
{
    public List<QuestionAnswer> Questions { get; set; } = new();
}

/// <summary>
/// A question and its answer from the session.
/// </summary>
public class QuestionAnswer
{
    public string Question { get; set; } = string.Empty;
    public string Speaker { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public int EntertainmentValue { get; set; } = 5;
}