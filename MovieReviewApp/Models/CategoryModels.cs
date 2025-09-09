namespace MovieReviewApp.Models
{
    public class CategoryResults
    {
        public TopFiveList? AIsUniqueObservations { get; set; }
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
        public List<QuestionAnswer> InitialQuestions { get; set; } = new();
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
        public double? StartTimeSeconds { get; set; }
        public double? EndTimeSeconds { get; set; }
    }

    public class QuestionAnswer
    {
        public string Question { get; set; } = string.Empty;
        public string Speaker { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public int EntertainmentValue { get; set; } // 1-10 scale
        public string AudioClipUrl { get; set; } = string.Empty;
    }

    public enum AudioQuality
    {
        Clear,
        Muffled,
        BackgroundNoise
    }
} 