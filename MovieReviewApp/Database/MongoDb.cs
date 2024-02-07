using MongoDB.Bson;
using MongoDB.Driver;
using MovieReviewApp.Models;

namespace MovieReviewApp.Database
{
    public class MongoDb
    {
        private IMongoDatabase? database;
        public MongoDb()
        {
            var mongoUri = "mongodb+srv://jaredbrowne:CjjTcmNP92xiL3we@moviereviewcluster.oi47y0a.mongodb.net/\r\n";
            IMongoClient client;
            client = new MongoClient(mongoUri);
            database = client.GetDatabase("MovieReview");
        }

        private IMongoCollection<MovieEvent> MovieEvents => database.GetCollection<MovieEvent>("MovieReviewCluster");

        public MovieEvent GetMovieEventBetweenDate(DateTime dt)
        {
            var filter = Builders<MovieEvent>.Filter.And(
                Builders<MovieEvent>.Filter.Lte("StartDate", dt),
                Builders<MovieEvent>.Filter.Gte("EndDate", dt)
            );
            return MovieEvents.Find<MovieEvent>(filter).FirstOrDefault();
        }

        public void AddOrUpdateMovieEvent(MovieEvent movieEvent)
        {
                var filter = Builders<MovieEvent>.Filter.Eq("Id", movieEvent.Id);
                var update = Builders<MovieEvent>.Update
                    .Set("StartDate", movieEvent.StartDate)
                    .Set("EndDate", movieEvent.EndDate)
                    .Set("Person", movieEvent.Person)
                    .Set("Movie", movieEvent.Movie)
                    .Set("DownloadLink", movieEvent.DownloadLink)
                    .Set("PosterUrl", movieEvent.PosterUrl)
                    .Set("IMDb", movieEvent.IMDb);
                MovieEvents.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });

        }

        public List<MovieEvent?> GetAllMovieEvents()
        {
            var sortDefinition = Builders<MovieEvent>.Sort.Ascending(me => me.StartDate);
            return MovieEvents.Find(_ => true)
                .Sort(sortDefinition)
                .ToList();
        }
    }
}
