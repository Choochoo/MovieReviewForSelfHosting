using MongoDB.Driver;
using MovieReviewApp.Enums;
using MovieReviewApp.Models;
using MovieReviewApp.Services;
using System.Linq.Expressions;

namespace MovieReviewApp.Database
{
    public class GenericMongoDb
    {
        private readonly IMongoDatabase? database;

        public GenericMongoDb(IConfiguration configuration, SecretsManager secretsManager, InstanceManager instanceManager)
        {
            try
            {
                // Get MongoDB connection string from instance secrets
                string mongoConnection = secretsManager.GetSecret("MongoDB:ConnectionString");

                if (!string.IsNullOrEmpty(mongoConnection))
                {
                    // Generate instance-specific database name to ensure complete isolation
                    var connectionBuilder = new MongoDB.Driver.MongoUrlBuilder(mongoConnection);
                    var instanceDbName = $"{connectionBuilder.DatabaseName ?? "moviereview"}_{instanceManager.InstanceName.ToLower().Replace("-", "_")}";
                    connectionBuilder.DatabaseName = instanceDbName;

                    var finalConnectionString = connectionBuilder.ToMongoUrl().ToString();

                    var client = new MongoClient(finalConnectionString);
                    database = client.GetDatabase(instanceDbName);
                    Console.WriteLine($"MongoDB connected successfully to instance database: {instanceDbName}");
                }
                else
                {
                    // MongoDB not configured yet - this is expected during first run setup
                    database = null;
                    Console.WriteLine("MongoDB not configured - database features will be unavailable until setup is complete");
                }
            }
            catch (Exception ex)
            {
                // Handle MongoDB connection errors gracefully
                database = null;
                Console.WriteLine($"MongoDB connection failed: {ex.Message}");
                Console.WriteLine("Database features will be unavailable - please check your MongoDB configuration");
            }
        }

        public bool IsConnected => database != null;

        // Generic collection accessor
        public IMongoCollection<T>? GetCollection<T>(CollectionType collectionType)
        {
            if (database == null) return null;

            return database.GetCollection<T>(collectionType.ToString());
        }

        // Generic CRUD operations
        public async Task<List<T>> GetAllAsync<T>(CollectionType collectionType)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return await collection.Find(_ => true).ToListAsync();
        }

        public List<T> GetAll<T>(CollectionType collectionType)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return collection.Find(_ => true).ToList();
        }

        public async Task<T?> GetByIdAsync<T>(CollectionType collectionType, Guid id) where T : BaseModel
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return null;

            return await collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        public T? GetById<T>(CollectionType collectionType, Guid id) where T : BaseModel
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return null;

            return collection.Find(x => x.Id == id).FirstOrDefault();
        }

        public async Task<List<T>> FindAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return await collection.Find(filter).ToListAsync();
        }

        public List<T> Find<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return collection.Find(filter).ToList();
        }

        public async Task InsertOneAsync<T>(CollectionType collectionType, T document)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return;

            await collection.InsertOneAsync(document);
        }

        public void InsertOne<T>(CollectionType collectionType, T document)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return;

            collection.InsertOne(document);
        }

        public async Task<bool> UpdateOneAsync<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public bool UpdateOne<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = collection.UpdateOne(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> UpsertOneAsync<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }

        public bool UpsertOne<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = collection.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }

        public async Task<bool> DeleteOneAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = await collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        public bool DeleteOne<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = collection.DeleteOne(filter);
            return result.DeletedCount > 0;
        }

        public async Task<long> DeleteManyAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            var result = await collection.DeleteManyAsync(filter);
            return result.DeletedCount;
        }

        public long DeleteMany<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            var result = collection.DeleteMany(filter);
            return result.DeletedCount;
        }

        public async Task<long> CountAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            return await collection.CountDocumentsAsync(filter);
        }

        public long Count<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            return collection.CountDocuments(filter);
        }

        public async Task ReplaceOneAsync<T>(CollectionType collectionType, Expression<Func<T, bool>> filterExpression, T replacement)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return;
            await collection.ReplaceOneAsync(filterExpression, replacement);
        }

        #region String Overloads for Backward Compatibility

        // These overloads accept string collection names for backward compatibility
        // Consider migrating to CollectionType enum for type safety

        private IMongoCollection<T>? GetCollection<T>(string collectionName)
        {
            if (database == null) return null;
            return database.GetCollection<T>(collectionName);
        }

        public async Task<List<T>> GetAllAsync<T>(string collectionName)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return new List<T>();
            return await collection.Find(_ => true).ToListAsync();
        }

        public async Task<T?> GetByIdAsync<T>(string collectionName, string id)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return default(T);
            
            var filter = Builders<T>.Filter.Eq("_id", id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task UpsertAsync<T>(string collectionName, T document)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return;

            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty != null)
            {
                var idValue = idProperty.GetValue(document);
                if (idValue != null)
                {
                    var filter = Builders<T>.Filter.Eq("_id", idValue.ToString());
                    await collection.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true });
                    return;
                }
            }

            // If no Id property or value, just insert
            await collection.InsertOneAsync(document);
        }

        public async Task<bool> DeleteAsync<T>(string collectionName, string id)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return false;

            var filter = Builders<T>.Filter.Eq("_id", id);
            var result = await collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        public async Task<List<T>> SearchAsync<T>(string collectionName, string searchField, string searchTerm)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return new List<T>();

            var filter = Builders<T>.Filter.Regex(searchField, new MongoDB.Bson.BsonRegularExpression(searchTerm, "i"));
            return await collection.Find(filter).ToListAsync();
        }

        #endregion
    }
}