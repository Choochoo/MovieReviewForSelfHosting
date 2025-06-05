using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("Settings")]
    public class Setting
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Key { get; set; }
        public required string Value { get; set; }
    }
}