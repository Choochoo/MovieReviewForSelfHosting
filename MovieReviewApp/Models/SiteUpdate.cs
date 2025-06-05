using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("SiteUpdates")]
    public class SiteUpdate : BaseModel
    {
        public DateTime LastUpdateTime { get; set; }
        public DateTime Date { get; set; }
        public DateTime Timestamp { get; set; }
        public string UpdateType { get; set; } // e.g., "MovieAdded", "MovieUpdated"
        public string Description { get; set; }
        public string UpdatedBy { get; set; } = "";
    }
}
