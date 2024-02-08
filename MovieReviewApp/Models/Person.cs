using System.ComponentModel.DataAnnotations.Schema;

namespace MovieReviewApp.Models
{
    public class Person
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Name { get; set; }
        [NotMapped]
        public bool IsEditing { get; set; }
    }
}
