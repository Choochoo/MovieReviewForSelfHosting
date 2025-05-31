using System.ComponentModel.DataAnnotations.Schema;

namespace MovieReviewApp.Models
{
    public class ImageStorage : BaseModel
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadDate { get; set; } = DateTime.UtcNow;
        public string? OriginalUrl { get; set; }
        public string? Hash { get; set; }
    }
}