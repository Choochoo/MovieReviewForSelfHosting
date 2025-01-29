using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieReviewApp.Models
{
    public class Phase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int Number { get; set; }
        [BsonIgnore]
        public List<MovieEvent> Events { get; set; } = new List<MovieEvent>();
        public string People { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
