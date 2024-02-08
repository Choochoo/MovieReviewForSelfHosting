namespace MovieReviewApp.Models
{
    public class Setting
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Key { get; set; }
        public string Value { get; set; }
    }
}