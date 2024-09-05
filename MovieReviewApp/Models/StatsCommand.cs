namespace MovieReviewApp.Models
{
    public class StatsCommand
    {
        public string Command { get; set; }
        public List<string> Results { get; set; }
        public DateTime ProcessedDate { get; set; }
        public string FolderName { get; set; }
    }
}
