using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("MovieSessions")]
    public class MovieSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Date { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public List<string> ParticipantsPresent { get; set; } = [];
        public List<string> ParticipantsAbsent { get; set; } = [];
        public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
        public List<AudioFile> AudioFiles { get; set; } = new();
        public CategoryResults CategoryResults { get; set; } = new();
        public SessionStats SessionStats { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<int, string> MicAssignments { get; set; } = new();
    }

    public class AudioFile
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
    }

    public class CategoryResults
    {
        public CategoryWinner? MostOffensiveTake { get; set; }
        public CategoryWinner? HottestTake { get; set; }
        public CategoryWinner? BiggestArgumentStarter { get; set; }
        public CategoryWinner? BestJoke { get; set; }
        public CategoryWinner? BestRoast { get; set; }
        public CategoryWinner? FunniestRandomTangent { get; set; }
        public CategoryWinner? MostPassionateDefense { get; set; }
        public CategoryWinner? BiggestUnanimousReaction { get; set; }
        public CategoryWinner? MostBoringStatement { get; set; }
        public CategoryWinner? BestPlotTwistRevelation { get; set; }
        public CategoryWinner? MovieSnobMoment { get; set; }
        public CategoryWinner? GuiltyPleasureAdmission { get; set; }
        public CategoryWinner? QuietestPersonBestMoment { get; set; }
        public TopFiveList? FunniestSentences { get; set; }
        public TopFiveList? MostBlandComments { get; set; }
    }

    public class CategoryWinner
    {
        public string Speaker { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Quote { get; set; } = string.Empty;
        public string Setup { get; set; } = string.Empty;
        public string GroupReaction { get; set; } = string.Empty;
        public string WhyItsGreat { get; set; } = string.Empty;
        public AudioQuality AudioQuality { get; set; } = AudioQuality.Clear;
        public string? AudioClipUrl { get; set; }
        public int EntertainmentScore { get; set; }
        public List<RunnerUp> RunnersUp { get; set; } = new();
    }

    public class RunnerUp
    {
        public string Speaker { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string BriefDescription { get; set; } = string.Empty;
        public int Place { get; set; }
    }

    public class TopFiveList
    {
        public List<TopFiveEntry> Entries { get; set; } = new();
    }

    public class TopFiveEntry
    {
        public int Rank { get; set; }
        public string Speaker { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Quote { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public AudioQuality AudioQuality { get; set; } = AudioQuality.Clear;
        public string? AudioClipUrl { get; set; }
        public double Score { get; set; }
        public string Reasoning { get; set; } = string.Empty;
        public string SourceAudioFile { get; set; } = string.Empty;
        public double StartTimeSeconds { get; set; }
        public double EndTimeSeconds { get; set; }
    }

    public class SessionStats
    {
        public string TotalDuration { get; set; } = string.Empty;
        public EnergyLevel EnergyLevel { get; set; } = EnergyLevel.Medium;
        public string TechnicalQuality { get; set; } = string.Empty;
        public int HighlightMoments { get; set; }
        public string BestMomentsSummary { get; set; } = string.Empty;
        public string AttendancePattern { get; set; } = string.Empty;
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

    public enum AudioQuality
    {
        Clear,
        Muffled,
        BackgroundNoise
    }

    public enum EnergyLevel
    {
        Low,
        Medium,
        High
    }

    public enum AudioProcessingStatus
    {
        Pending,           // Just uploaded, not processed yet
        PendingMp3,        // Needs MP3 conversion
        FailedMp3,         // MP3 conversion failed
        ProcessedMp3,      // Successfully converted to MP3
        UploadedToGladia,  // Uploaded to Gladia for transcription
        TranscriptionComplete, // Gladia transcription successful
        Failed             // General processing failure
    }

    public class ProcessingQueueItem
    {
        public string SessionId { get; set; } = string.Empty;
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