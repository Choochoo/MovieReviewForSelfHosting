using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using MovieReviewApp.Attributes;

namespace MovieReviewApp.Models
{
    [MongoCollection("People")]
    public class Person : BaseModel
    {
        public int Order { get; set; }
        public string? Name { get; set; }
        public int MicNumber { get; set; }
        [BsonIgnore]
        public bool IsEditing { get; set; }
    }
}
