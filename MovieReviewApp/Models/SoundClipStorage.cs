using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("SoundClipStorage")]
    public class SoundClipStorage : BaseModel
    {
        public string PersonId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public byte[] AudioData { get; set; } = Array.Empty<byte>();
        public long FileSize { get; set; }
        public double? Duration { get; set; }
        public DateTime UploadDate { get; set; } = DateTime.UtcNow;
        public string? OriginalUrl { get; set; }
        public string? Hash { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Description { get; set; }
    }
}