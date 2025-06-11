using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    public abstract class BaseModel
    {
        [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [MongoCollection("MovieEvents")]
    public class MovieEvent : BaseModel
    {
        public int? PhaseNumber { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? Person { get; set; }
        public string? Movie { get; set; }
        public string? DownloadLink { get; set; }
        public string? PosterUrl { get; set; }
        [BsonGuidRepresentation(GuidRepresentation.CSharpLegacy)]
        public Guid? ImageId { get; set; }
        public string? IMDb { get; set; }
        public string? Reasoning { get; set; }
        public bool AlreadySeen { get; set; }
        public DateTime? SeenDate { get; set; }
        public DateTime? MeetupTime { get; set; }
        public string? Synopsis { get; set; } = null;

        [NotMapped]
        [BsonIgnore]
        public bool FromDatabase { get; set; }
        [NotMapped]
        [BsonIgnore]
        public bool IsEditing { get; set; }
    }
}
