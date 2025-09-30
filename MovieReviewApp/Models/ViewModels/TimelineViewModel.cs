namespace MovieReviewApp.Models.ViewModels;

/// <summary>
/// Single timeline item - either cache-based (future) or database-based (past/current)
/// </summary>
public class TimelineItem
{
    public DateTime Month { get; set; }
    public string AssignedPersonName { get; set; } = string.Empty;
    public bool IsAwardsEvent { get; set; }
    public int? AwardsEventNumber { get; set; }

    // Database enrichment (null if future/not created yet)
    public Guid? MovieEventId { get; set; }
    public string? MovieTitle { get; set; }
    public string? MoviePoster { get; set; }
    public bool HasRecording { get; set; }
    public bool HasTranscription { get; set; }

    // Computed state
    public TimelineItemState State { get; set; }
}

public enum TimelineItemState
{
    Past,      // Before current month
    Current,   // This month
    Future     // After current month
}

/// <summary>
/// Phase grouping - calculated from cache, not stored in database
/// </summary>
public class TimelinePhase
{
    public int PhaseNumber { get; set; }
    public DateTime StartMonth { get; set; }
    public DateTime EndMonth { get; set; }
    public List<TimelineItem> Items { get; set; } = new();
    public int TotalEvents { get; set; }
    public int CompletedEvents { get; set; }
    public bool IsCurrentPhase { get; set; }
}

/// <summary>
/// Complete timeline view model for rendering
/// </summary>
public class TimelineViewModel
{
    public TimelinePhase? CurrentPhase { get; set; }
    public List<TimelinePhase> FuturePhases { get; set; } = new();
    public List<TimelinePhase> PastPhases { get; set; } = new();
}
