namespace MovieReviewApp.Models
{
    public class AppSettings
    {
        public string Title { get; set; } = "";
        public string ContentType { get; set; } = "General"; // "General" or "Family"
        public bool IsFamilyFriendly => ContentType.Equals("Family", StringComparison.OrdinalIgnoreCase);
    }
}
