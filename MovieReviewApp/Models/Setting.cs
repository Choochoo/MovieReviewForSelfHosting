namespace MovieReviewApp.Models
{
    public class Setting : BaseModel
    {
        public required string Key { get; set; }
        public required string Value { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}