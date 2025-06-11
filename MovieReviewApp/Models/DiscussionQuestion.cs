using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("DiscussionQuestions")]
    public class DiscussionQuestion : BaseModel
    {
        public string Question { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;

        // UI-only property for editing state
        public bool IsEditing { get; set; } = false;
    }
}
