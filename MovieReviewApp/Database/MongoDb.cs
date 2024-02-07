using MongoDB.Bson;
using MongoDB.Driver;
using MovieReviewApp.Models;
using System;


namespace MovieReviewApp.Database
{
    public class MongoDb
    {
        private IMongoDatabase? database;
        public MongoDb()
        {
            string mongoUri = Environment.GetEnvironmentVariable("MONGO_URI");
            if (string.IsNullOrEmpty(mongoUri))
            {
                throw new ArgumentNullException("MongoDB connection string not found in environment variables");
            }
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
