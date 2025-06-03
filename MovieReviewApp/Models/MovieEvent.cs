using System.ComponentModel.DataAnnotations.Schema;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    public abstract class BaseModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
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
        public Guid? ImageId { get; set; }
        public string? IMDb { get; set; }
        public string? Reasoning { get; set; }
        public bool AlreadySeen { get; set; }
        public DateTime? SeenDate { get; set; }
        public DateTime? MeetupTime { get; set; }
        public string? Synopsis { get; set; } = null;

        [NotMapped]
        public bool FromDatabase { get; set; }
        [NotMapped]
        public bool IsEditing { get; set; }
    }
}
