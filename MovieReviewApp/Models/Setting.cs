using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("Settings")]
    public class Setting : BaseModel
    {
        public required string Key { get; set; }
        public required string Value { get; set; }
    }
}