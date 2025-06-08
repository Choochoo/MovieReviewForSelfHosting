using MovieReviewApp.Attributes;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieReviewApp.Models
{
    [MongoCollection("People")]
    public class Person : BaseModel
    {
        public int Order { get; set; }
        public string? Name { get; set; }
        public DateTime? UpdatedAt { get; set; }
        [NotMapped]
        public bool IsEditing { get; set; }
    }
}
