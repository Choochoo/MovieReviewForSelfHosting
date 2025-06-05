using MongoDB.Bson.Serialization.Attributes;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("Phases")]
    public class Phase : BaseModel
    {
        public int Number { get; set; }
        [BsonIgnore]
        public List<MovieEvent> Events { get; set; } = new List<MovieEvent>();
        public string People { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
