using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("StatsCommands")]
    public class StatsCommand
    {
        public string Command { get; set; } = "";
        public List<string> Results { get; set; } = new List<string>();
        public DateTime ProcessedDate { get; set; }
        public string FolderName { get; set; } = "";
    }
}
