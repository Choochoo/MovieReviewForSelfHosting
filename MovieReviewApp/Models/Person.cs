using System.ComponentModel.DataAnnotations.Schema;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("People")]
    public class Person
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int Order { get; set; }
        public string? Name { get; set; }
        [NotMapped]
        public bool IsEditing { get; set; }
    }
}
