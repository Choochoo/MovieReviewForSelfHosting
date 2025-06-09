using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("MovieSessions")]
    public class MovieSession : BaseModel
    {
        public DateTime Date { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public List<string> ParticipantsPresent { get; set; } = [];
        public List<string> ParticipantsAbsent { get; set; } = [];
        public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
        public List<AudioFile> AudioFiles { get; set; } = new();
        public SessionStats SessionStats { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public string? ErrorMessage { get; set; }
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<int, string> MicAssignments { get; set; } = new();
        public CategoryResults CategoryResults { get; set; } = new();
    }

    public class AudioFile : BaseModel
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int? SpeakerNumber { get; set; }
        public bool IsMasterRecording { get; set; }
        public long FileSize { get; set; }
        public TimeSpan? Duration { get; set; }
        public string? AudioUrl { get; set; }
        public string? TranscriptId { get; set; }
        public string? TranscriptText { get; set; }
        public AudioProcessingStatus ProcessingStatus { get; set; } = AudioProcessingStatus.Pending;
        public string? Mp3FilePath { get; set; }
        public long? Mp3FileSize { get; set; }
        public string? ConversionError { get; set; }
        public DateTime? ConvertedAt { get; set; }
        public DateTime? UploadedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? JsonFilePath { get; set; }
        public int ProgressPercentage { get; set; } = 0;
        public string? CurrentStep { get; set; } = "Waiting to upload";
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public bool CanRetry { get; set; } = false;
    }

    public class SessionStats
    {
        public string TotalDuration { get; set; } = string.Empty;
        public EnergyLevel EnergyLevel { get; set; } = EnergyLevel.Medium;
        public string TechnicalQuality { get; set; } = string.Empty;
        public int HighlightMoments { get; set; }
        public string BestMomentsSummary { get; set; } = string.Empty;
        public string AttendancePattern { get; set; } = string.Empty;

        // Detailed conversation statistics
        public Dictionary<string, int> WordCounts { get; set; } = new();
        public Dictionary<string, int> QuestionCounts { get; set; } = new();
        public Dictionary<string, int> InterruptionCounts { get; set; } = new();
        public Dictionary<string, int> LaughterCounts { get; set; } = new();
        public Dictionary<string, int> CurseWordCounts { get; set; } = new();
        public string MostTalkativePerson { get; set; } = string.Empty;
        public string QuietestPerson { get; set; } = string.Empty;
        public string MostInquisitivePerson { get; set; } = string.Empty;
        public string BiggestInterruptor { get; set; } = string.Empty;
        public string FunniestPerson { get; set; } = string.Empty;
        public string MostProfanePerson { get; set; } = string.Empty;
        public int TotalInterruptions { get; set; }
        public int TotalQuestions { get; set; }
        public int TotalLaughterMoments { get; set; }
        public int TotalCurseWords { get; set; }
        public string ConversationTone { get; set; } = string.Empty;

        // Initial discussion questions and answers
        public List<QuestionAnswer> InitialQuestions { get; set; } = new();
    }

    public enum ProcessingStatus
    {
        Pending,
        Validating,
        Transcribing,
        Analyzing,
        Complete,
        Failed
    }

    public enum EnergyLevel
    {
        Low,
        Medium,
        High
    }

    public enum AudioProcessingStatus
    {
        Pending,              // Just uploaded, not processed yet
        Uploading,           // Currently uploading the file
        ConvertingToMp3,      // Currently converting WAV to MP3
        PendingMp3,           // MP3 conversion complete, ready for upload
        FailedMp3,            // MP3 conversion failed
        ProcessedMp3,         // Successfully converted to MP3
        UploadingToGladia,    // Currently uploading to Gladia
        UploadedToGladia,     // Uploaded to Gladia for transcription
        Transcribing,         // Gladia is processing transcription
        TranscriptionComplete, // Gladia transcription successful
        ProcessingWithAI,     // OpenAI analysis in progress
        Complete,             // All processing complete
        Failed                // General processing failure
    }

    public class ProcessingQueueItem : BaseModel
    {
        public Guid SessionId { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public string? CurrentStep { get; set; }
        public int Progress { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class SpeakerProfile
    {
        public int SpeakerNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public SpeakerStats Stats { get; set; } = new();
    }

    public class SpeakerStats
    {
        public int ComedyWins { get; set; }
        public int HotTakes { get; set; }
        public int MovieInsights { get; set; }
        public int ArgumentsWon { get; set; }
        public int RoastsDelivered { get; set; }
        public int ControversialTakes { get; set; }
        public int SnobMoments { get; set; }
        public int GuiltyPleasures { get; set; }
        public int PassionateDefenses { get; set; }
        public int QuietMoments { get; set; }
        public List<string> FavoriteGenres { get; set; } = new();
        public List<string> CommonDebatePartners { get; set; } = new();
    }
}