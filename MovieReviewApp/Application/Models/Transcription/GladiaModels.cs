namespace MovieReviewApp.Application.Models.Transcription;

/// <summary>
/// Response model from Gladia API for transcription requests
/// </summary>
public class GladiaTranscriptionResponse
{
    public string id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public GladiaTranscriptionResult? result { get; set; }
}

/// <summary>
/// Result container for Gladia transcription response
/// </summary>
public class GladiaTranscriptionResult
{
    public GladiaTranscriptionData? transcription { get; set; }
}

/// <summary>
/// Main transcription data from Gladia API
/// </summary>
public class GladiaTranscriptionData
{
    public string full_transcript { get; set; } = string.Empty;
    public List<WordCountUtterance> utterances { get; set; } = new();
}

/// <summary>
/// Enhanced utterance model with speaker name resolution and matching metadata
/// </summary>
public class EnhancedUtterance : WordCountUtterance
{
    public new string speaker { get; set; } = "Unknown"; // Override with string type instead of int
    public int speaker_number { get; set; } // Original speaker number from Gladia
    public int? matched_from_mic { get; set; } // Which mic this utterance was matched from
    public double? match_score { get; set; } // Confidence score for mic matching
}

/// <summary>
/// Enhanced transcription response with speaker name resolution
/// </summary>
public class EnhancedTranscriptionResponse
{
    public string id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public EnhancedTranscriptionResult? result { get; set; }
    public TranscriptionMetadata? metadata { get; set; }
}

/// <summary>
/// Enhanced result container with speaker name resolution
/// </summary>
public class EnhancedTranscriptionResult
{
    public EnhancedTranscriptionData? transcription { get; set; }
}

/// <summary>
/// Enhanced transcription data with resolved speaker names
/// </summary>
public class EnhancedTranscriptionData
{
    public string full_transcript { get; set; } = string.Empty;
    public List<EnhancedUtterance> utterances { get; set; } = new();
}

/// <summary>
/// Metadata about the transcription process
/// </summary>
public class TranscriptionMetadata
{
    public string session_id { get; set; } = string.Empty;
    public string movie_title { get; set; } = string.Empty;
    public DateTime processed_at { get; set; }
    public Dictionary<int, string> speaker_assignments { get; set; } = new();
    public bool soundboard_filtered { get; set; }
    public int original_utterance_count { get; set; }
    public int filtered_utterance_count { get; set; }
}