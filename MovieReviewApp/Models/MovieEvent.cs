using System.ComponentModel.DataAnnotations.Schema;

namespace MovieReviewApp.Models
{
    public class MovieEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? Person { get; set; }
        public string? Movie { get; set; }
        public string? DownloadLink { get; set; }
        public string? PosterUrl { get; set; }
        public string? IMDb { get; set; }
        public string? Reasoning { get; set; }

        [NotMapped]
        public bool FromDatabase { get; set; }
        [NotMapped]
        public bool IsEditing { get; set; }
    }
}
