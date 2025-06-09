namespace MovieReviewApp.Application.Models.Transcription;

/// <summary>
/// Contains transcript data with utterances from speakers.
/// </summary>
public class TranscriptData
{
    public List<WordCountUtterance> utterances { get; set; } = new List<WordCountUtterance>();
}

/// <summary>
/// Represents a single utterance in a transcript with timing and speaker information.
/// </summary>
public class WordCountUtterance
{
    public double start { get; set; }
    public double end { get; set; }
    public string text { get; set; } = string.Empty;
    public int speaker { get; set; }
    public double confidence { get; set; }
    public List<WordCountWord>? words { get; set; }
}

/// <summary>
/// Represents a single word with precise timing information.
/// </summary>
public class WordCountWord
{
    public string word { get; set; } = string.Empty;
    public double start { get; set; }
    public double end { get; set; }
    public double confidence { get; set; }
}