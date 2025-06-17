namespace MovieReviewApp.Models
{
    public class Setting : BaseModel
    {
        public required string Key { get; set; }
        public required string Value { get; set; }
    }

    public class ApplicationSettings : BaseModel
    {
        // Theme Settings
        public string DefaultTheme { get; set; } = "cyberpunk";
        public List<string> AvailableThemes { get; set; } = new() { "cyberpunk", "ocean", "nature", "classic" };
        
        // File Upload Settings  
        public long MaxFileUploadSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10GB
        
        // API Timeout Settings
        public int OpenAITimeoutMinutes { get; set; } = 5;
        public int GladiaTimeoutHours { get; set; } = 1;
        
        // Processing Settings
        public int MaxTranscriptSize { get; set; } = 60000;
        
        // Demo Settings
        public bool IsDemoMode { get; set; } = false;
        public bool AllowDemoDataModification { get; set; } = false;
        
        // Award Settings Defaults
        public int DefaultPhasesBeforeAward { get; set; } = 2;
        public bool DefaultAwardsEnabled { get; set; } = false;
        public bool DefaultShowResultsDuringVoting { get; set; } = false;
        public bool DefaultAllowVoteChanges { get; set; } = true;
        public int DefaultVoteChangeTimeLimit { get; set; } = 24; // hours
    }
}