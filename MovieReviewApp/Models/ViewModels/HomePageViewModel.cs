using MovieReviewApp.Models;

namespace MovieReviewApp.Models.ViewModels
{
    /// <summary>
    /// View model containing all data needed for the Home page
    /// </summary>
    public class HomePageViewModel
    {
        // Current state
        public MovieEvent? CurrentEvent { get; set; }
        public MovieEvent? NextEvent { get; set; }
        public bool IsShowingPastEvent { get; set; }
        
        // Award data
        public bool IsCurrentPhaseAwardPhase { get; set; }
        public AwardSetting? AwardSettings { get; set; }
        public List<AwardEvent> AllAwardEvents { get; set; } = new();
        public List<AwardQuestion> AllAwardQuestions { get; set; } = new();
        public Dictionary<(Guid, Guid), List<QuestionResult>> QuestionResults { get; set; } = new();
        
        // Basic data
        public List<DiscussionQuestion> DiscussionQuestions { get; set; } = new();
        public List<Person> AllPeople { get; set; } = new();
        public List<Setting> Settings { get; set; } = new();
        public List<MovieEvent> ExistingEvents { get; set; } = new();
        public int ExistingEventCount { get; set; }
        public List<Phase> DbPhases { get; set; } = new();
        public List<SiteUpdate> RecentUpdates { get; set; } = new();
        
        // Derived/computed properties
        public DateTime? StartDate { get; set; }
        public bool RespectOrder { get; set; }
        public string[] AllNames { get; set; } = Array.Empty<string>();
        
        // Generated phases for display
        public List<Phase> GeneratedPhases { get; set; } = new();
    }
}