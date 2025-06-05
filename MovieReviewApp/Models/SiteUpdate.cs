using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("SiteUpdates")]
    public class SiteUpdate
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime LastUpdateTime { get; set; }
        public string UpdateType { get; set; } // e.g., "MovieAdded", "MovieUpdated"
        public string Description { get; set; }
    }
}
