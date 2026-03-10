using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models;

[MongoCollection("OratorRules")]
public class OratorRule : BaseModel
{
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }

    // UI-only property for editing state
    public bool IsEditing { get; set; } = false;
}
