using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("DiscussionQuestions")]
    public class DiscussionQuestion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Question { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        
        // UI-only property for editing state
        public bool IsEditing { get; set; } = false;
    }
}