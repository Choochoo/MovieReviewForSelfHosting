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

        private IMongoCollection<MovieEvent> MovieEvents => database.GetCollection<MovieEvent>("MovieReviews");
        private IMongoCollection<Person> People => database.GetCollection<Person>("People");
        private IMongoCollection<Setting> Settings => database.GetCollection<Setting>("Settings");

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

        private List<Person> QueryPeople()
        {
            var sortDefinition = Builders<Person>.Sort.Ascending(me => me.Name);
            return People.Find(_ => true)
                .Sort(sortDefinition)
                .ToList();
        }

        public List<Person> GetAllPeople()
        {
            var people = QueryPeople();
            if (people.Count == 0)
            {
                AddPerson(new Person { Name = "Jeremiah" });
                AddPerson(new Person { Name = "Lacey" });
                AddPerson(new Person { Name = "Jared" });
                AddPerson(new Person { Name = "Dave" });
                AddPerson(new Person { Name = "Keri" });
                people = QueryPeople();
            }
            return people;
        }

        private List<Setting> QuerySetting()
        {
            return Settings.Find(_ => true).ToList();
        }

        public List<Setting> GetSettings()
        {
            var settings = QuerySetting();
            if (settings.Count == 0)
            {
                AddOrUpdateSetting(new Setting { Key = "StartDate", Value = (new DateTime(2024, 1, 1)).ToString() });
                AddOrUpdateSetting(new Setting { Key = "TimeCount", Value = "1" });
                AddOrUpdateSetting(new Setting { Key = "TimePeriod", Value = "Month" });

                settings = QuerySetting();
            }
            return settings;
        }

        internal void AddOrUpdateSetting(Setting setting)
        {
            var filter = Builders<Setting>.Filter.Eq("Id", setting.Id);
            var update = Builders<Setting>.Update
                .Set("Key", setting.Key)
                .Set("Value", setting.Value);
            Settings.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }

        public void AddPerson(Person person)
        {
            People.InsertOne(person);
        }

        public void DeletePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq("Id", person.Id);
            People.DeleteOne(filter);
        }

        internal void AddOrUpdatePerson(Person person)
        {
            var filter = Builders<Person>.Filter.Eq("Id", person.Id);
            var update = Builders<Person>.Update
                .Set("Name", person.Name);
            People.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }
    }
}
