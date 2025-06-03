using MongoDB.Driver;
using MongoDB.Bson;
using MovieReviewApp.Attributes;
using MovieReviewApp.Models;
using MovieReviewApp.Services;
using MovieReviewApp.Enums;
using System.Linq.Expressions;
using System.Reflection;
using System.Collections.Concurrent;

namespace MovieReviewApp.Database
{
    /// <summary>
    /// Refactored MongoDB service that uses type-based collection resolution
    /// </summary>
    public class MongoDbService
    {
        private readonly IMongoDatabase? _database;
        private readonly ILogger<MongoDbService> _logger;
        
        // Cache collection names for performance
        private static readonly ConcurrentDictionary<Type, string> _collectionNameCache = new();

        public MongoDbService(
            IConfiguration configuration, 
            SecretsManager secretsManager, 
            InstanceManager instanceManager,
            ILogger<MongoDbService> logger)
        {
            _logger = logger;
            
            try
            {
                string mongoConnection = secretsManager.GetSecret("MongoDB:ConnectionString");

                if (!string.IsNullOrEmpty(mongoConnection))
                {
                    var connectionBuilder = new MongoUrlBuilder(mongoConnection);
                    var instanceDbName = $"{connectionBuilder.DatabaseName ?? "moviereview"}_{instanceManager.InstanceName.ToLower().Replace("-", "_")}";
                    connectionBuilder.DatabaseName = instanceDbName;

                    var finalConnectionString = connectionBuilder.ToMongoUrl().ToString();
                    var client = new MongoClient(finalConnectionString);
                    _database = client.GetDatabase(instanceDbName);
                    
                    _logger.LogInformation("MongoDB connected successfully to instance database: {DatabaseName}", instanceDbName);
                }
                else
                {
                    _database = null;
                    _logger.LogWarning("MongoDB not configured - database features will be unavailable until setup is complete");
                }
            }
            catch (Exception ex)
            {
                _database = null;
                _logger.LogError(ex, "MongoDB connection failed");
            }
        }

        public bool IsConnected => _database != null;

        /// <summary>
        /// Gets the collection name for a type using attributes or conventions
        /// </summary>
        private string GetCollectionName<T>()
        {
            var type = typeof(T);
            
            return _collectionNameCache.GetOrAdd(type, t =>
            {
                // Check for MongoCollection attribute
                var attribute = t.GetCustomAttribute<MongoCollectionAttribute>();
                if (attribute != null)
                {
                    return attribute.CollectionName;
                }

                // Use convention: ClassName -> ClassNames (with proper pluralization)
                var name = t.Name;
                
                // Handle special cases for English pluralization
                if (name.EndsWith("y") && !name.EndsWith("ay") && !name.EndsWith("ey") && !name.EndsWith("oy") && !name.EndsWith("uy"))
                {
                    return name.Substring(0, name.Length - 1) + "ies"; // e.g., Category -> Categories
                }
                else if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("ch") || name.EndsWith("sh"))
                {
                    return name + "es"; // e.g., Class -> Classes
                }
                else if (name.EndsWith("Person"))
                {
                    return name.Replace("Person", "People"); // Special case: Person -> People
                }
                else
                {
                    return name + "s"; // Default: just add 's'
                }
            });
        }

        /// <summary>
        /// Gets a MongoDB collection for the specified type
        /// </summary>
        private IMongoCollection<T>? GetCollection<T>()
        {
            if (_database == null) return null;
            
            var collectionName = GetCollectionName<T>();
            return _database.GetCollection<T>(collectionName);
        }

        /// <summary>
        /// Gets a MongoDB collection using CollectionType enum
        /// </summary>
        public IMongoCollection<T>? GetCollection<T>(CollectionType collectionType)
        {
            if (_database == null) return null;
            
            return _database.GetCollection<T>(collectionType.ToString());
        }

        #region CRUD Operations - Type-based only!

        /// <summary>
        /// Gets all documents of type T
        /// </summary>
        public async Task<List<T>> GetAllAsync<T>()
        {
            var collection = GetCollection<T>();
            if (collection == null) return new List<T>();

            return await collection.Find(_ => true).ToListAsync();
        }

        /// <summary>
        /// Gets a document by ID (supports both string and Guid)
        /// </summary>
        public async Task<T?> GetByIdAsync<T>(object id)
        {
            var collection = GetCollection<T>();
            if (collection == null) return default;

            FilterDefinition<T> filter;
            
            // Handle different ID types
            if (id is Guid guidId)
            {
                filter = Builders<T>.Filter.Eq("_id", guidId);
            }
            else if (id is string stringId)
            {
                // Try to parse as Guid first
                if (Guid.TryParse(stringId, out var parsedGuid))
                {
                    filter = Builders<T>.Filter.Eq("_id", parsedGuid);
                }
                else
                {
                    filter = Builders<T>.Filter.Eq("_id", stringId);
                }
            }
            else
            {
                filter = Builders<T>.Filter.Eq("_id", id);
            }

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Finds documents matching a filter expression
        /// </summary>
        public async Task<List<T>> FindAsync<T>(Expression<Func<T, bool>> filter)
        {
            var collection = GetCollection<T>();
            if (collection == null) return new List<T>();

            return await collection.Find(filter).ToListAsync();
        }

        /// <summary>
        /// Finds a single document matching a filter expression
        /// </summary>
        public async Task<T?> FindOneAsync<T>(Expression<Func<T, bool>> filter)
        {
            var collection = GetCollection<T>();
            if (collection == null) return default;

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Inserts a new document
        /// </summary>
        public async Task<T> InsertAsync<T>(T document)
        {
            var collection = GetCollection<T>();
            if (collection == null) 
                throw new InvalidOperationException("Database not connected");

            await collection.InsertOneAsync(document);
            return document;
        }

        /// <summary>
        /// Inserts multiple documents
        /// </summary>
        public async Task InsertManyAsync<T>(IEnumerable<T> documents)
        {
            var collection = GetCollection<T>();
            if (collection == null) 
                throw new InvalidOperationException("Database not connected");

            await collection.InsertManyAsync(documents);
        }

        /// <summary>
        /// Updates or inserts a document based on its ID
        /// </summary>
        public async Task<T> UpsertAsync<T>(T document)
        {
            var collection = GetCollection<T>();
            if (collection == null) 
                throw new InvalidOperationException("Database not connected");

            // Get ID value using reflection
            var idProperty = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("_id");
            if (idProperty == null)
                throw new InvalidOperationException($"Type {typeof(T).Name} must have an Id or _id property");

            var idValue = idProperty.GetValue(document);
            if (idValue == null)
            {
                // If no ID, just insert
                await collection.InsertOneAsync(document);
                return document;
            }

            var filter = Builders<T>.Filter.Eq("_id", idValue);
            var options = new ReplaceOptions { IsUpsert = true };
            await collection.ReplaceOneAsync(filter, document, options);
            
            return document;
        }

        /// <summary>
        /// Updates a single document matching the filter
        /// </summary>
        public async Task<bool> UpdateOneAsync<T>(
            Expression<Func<T, bool>> filter, 
            UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>();
            if (collection == null) return false;

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Updates multiple documents matching the filter
        /// </summary>
        public async Task<long> UpdateManyAsync<T>(
            Expression<Func<T, bool>> filter, 
            UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>();
            if (collection == null) return 0;

            var result = await collection.UpdateManyAsync(filter, update);
            return result.ModifiedCount;
        }

        /// <summary>
        /// Deletes a document by ID
        /// </summary>
        public async Task<bool> DeleteByIdAsync<T>(object id)
        {
            var collection = GetCollection<T>();
            if (collection == null) return false;

            FilterDefinition<T> filter;
            
            if (id is Guid guidId)
            {
                filter = Builders<T>.Filter.Eq("_id", guidId);
            }
            else if (id is string stringId && Guid.TryParse(stringId, out var parsedGuid))
            {
                filter = Builders<T>.Filter.Eq("_id", parsedGuid);
            }
            else
            {
                filter = Builders<T>.Filter.Eq("_id", id);
            }

            var result = await collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Deletes documents matching a filter
        /// </summary>
        public async Task<long> DeleteManyAsync<T>(Expression<Func<T, bool>> filter)
        {
            var collection = GetCollection<T>();
            if (collection == null) return 0;

            var result = await collection.DeleteManyAsync(filter);
            return result.DeletedCount;
        }

        /// <summary>
        /// Counts documents matching a filter
        /// </summary>
        public async Task<long> CountAsync<T>(Expression<Func<T, bool>>? filter = null)
        {
            var collection = GetCollection<T>();
            if (collection == null) return 0;

            if (filter == null)
            {
                return await collection.CountDocumentsAsync(_ => true);
            }

            return await collection.CountDocumentsAsync(filter);
        }

        /// <summary>
        /// Checks if any documents match the filter
        /// </summary>
        public async Task<bool> AnyAsync<T>(Expression<Func<T, bool>>? filter = null)
        {
            var count = await CountAsync(filter);
            return count > 0;
        }

        /// <summary>
        /// Performs a text search on specified fields
        /// </summary>
        public async Task<List<T>> SearchTextAsync<T>(string searchTerm, params Expression<Func<T, object>>[] searchFields)
        {
            var collection = GetCollection<T>();
            if (collection == null) return new List<T>();

            var filters = new List<FilterDefinition<T>>();
            
            foreach (var field in searchFields)
            {
                var fieldName = GetFieldName(field);
                var filter = Builders<T>.Filter.Regex(fieldName, new BsonRegularExpression(searchTerm, "i"));
                filters.Add(filter);
            }

            var combinedFilter = filters.Any() 
                ? Builders<T>.Filter.Or(filters) 
                : Builders<T>.Filter.Empty;

            return await collection.Find(combinedFilter).ToListAsync();
        }

        /// <summary>
        /// Gets paginated results
        /// </summary>
        public async Task<(List<T> items, long totalCount)> GetPagedAsync<T>(
            int page, 
            int pageSize, 
            Expression<Func<T, bool>>? filter = null,
            Expression<Func<T, object>>? orderBy = null,
            bool descending = false)
        {
            var collection = GetCollection<T>();
            if (collection == null) return (new List<T>(), 0);

            var query = filter == null 
                ? collection.Find(_ => true) 
                : collection.Find(filter);

            var totalCount = await query.CountDocumentsAsync();

            if (orderBy != null)
            {
                query = descending 
                    ? query.SortByDescending(orderBy) 
                    : query.SortBy(orderBy);
            }

            var items = await query
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        #endregion

        #region CollectionType Overloads for Backward Compatibility

        /// <summary>
        /// Gets all documents from a collection specified by CollectionType
        /// </summary>
        public async Task<List<T>> GetAllAsync<T>(CollectionType collectionType)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return await collection.Find(_ => true).ToListAsync();
        }

        /// <summary>
        /// Gets all documents from a collection (sync version)
        /// </summary>
        public List<T> GetAll<T>(CollectionType collectionType)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return collection.Find(_ => true).ToList();
        }

        /// <summary>
        /// Gets a document by ID from a collection specified by CollectionType
        /// </summary>
        public async Task<T?> GetByIdAsync<T>(CollectionType collectionType, Guid id) where T : BaseModel
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return null;

            return await collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Gets a document by ID (sync version)
        /// </summary>
        public T? GetById<T>(CollectionType collectionType, Guid id) where T : BaseModel
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return null;

            return collection.Find(x => x.Id == id).FirstOrDefault();
        }

        /// <summary>
        /// Finds documents matching a filter
        /// </summary>
        public async Task<List<T>> FindAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return await collection.Find(filter).ToListAsync();
        }

        /// <summary>
        /// Finds documents matching a filter (sync version)
        /// </summary>
        public List<T> Find<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return new List<T>();

            return collection.Find(filter).ToList();
        }

        /// <summary>
        /// Inserts a document into a collection
        /// </summary>
        public async Task InsertOneAsync<T>(CollectionType collectionType, T document)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return;

            await collection.InsertOneAsync(document);
        }

        /// <summary>
        /// Inserts a document (sync version)
        /// </summary>
        public void InsertOne<T>(CollectionType collectionType, T document)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return;

            collection.InsertOne(document);
        }

        /// <summary>
        /// Updates a document in a collection
        /// </summary>
        public async Task<bool> UpdateOneAsync<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Updates a document (sync version)
        /// </summary>
        public bool UpdateOne<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = collection.UpdateOne(filter, update);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Upserts a document in a collection
        /// </summary>
        public async Task<bool> UpsertOneAsync<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }

        /// <summary>
        /// Upserts a document (sync version)
        /// </summary>
        public bool UpsertOne<T>(CollectionType collectionType, FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = collection.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }

        /// <summary>
        /// Deletes a document from a collection
        /// </summary>
        public async Task<bool> DeleteOneAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = await collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Deletes a document (sync version)
        /// </summary>
        public bool DeleteOne<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return false;

            var result = collection.DeleteOne(filter);
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Deletes multiple documents from a collection
        /// </summary>
        public async Task<long> DeleteManyAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            var result = await collection.DeleteManyAsync(filter);
            return result.DeletedCount;
        }

        /// <summary>
        /// Deletes multiple documents (sync version)
        /// </summary>
        public long DeleteMany<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            var result = collection.DeleteMany(filter);
            return result.DeletedCount;
        }

        /// <summary>
        /// Counts documents in a collection
        /// </summary>
        public async Task<long> CountAsync<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            return await collection.CountDocumentsAsync(filter);
        }

        /// <summary>
        /// Counts documents (sync version)
        /// </summary>
        public long Count<T>(CollectionType collectionType, FilterDefinition<T> filter)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return 0;

            return collection.CountDocuments(filter);
        }

        /// <summary>
        /// Replaces a document in a collection
        /// </summary>
        public async Task ReplaceOneAsync<T>(CollectionType collectionType, Expression<Func<T, bool>> filterExpression, T replacement)
        {
            var collection = GetCollection<T>(collectionType);
            if (collection == null) return;
            await collection.ReplaceOneAsync(filterExpression, replacement);
        }

        #endregion

        #region String Overloads for Backward Compatibility

        /// <summary>
        /// Gets a collection by string name (for backward compatibility)
        /// </summary>
        private IMongoCollection<T>? GetCollection<T>(string collectionName)
        {
            if (_database == null) return null;
            return _database.GetCollection<T>(collectionName);
        }

        /// <summary>
        /// Gets all documents from a collection by string name
        /// </summary>
        public async Task<List<T>> GetAllAsync<T>(string collectionName)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return new List<T>();
            return await collection.Find(_ => true).ToListAsync();
        }

        /// <summary>
        /// Gets a document by ID from a collection by string name
        /// </summary>
        public async Task<T?> GetByIdAsync<T>(string collectionName, string id)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return default(T);
            
            var filter = Builders<T>.Filter.Eq("_id", id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Upserts a document in a collection by string name
        /// </summary>
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

        /// <summary>
        /// Deletes a document by ID from a collection by string name
        /// </summary>
        public async Task<bool> DeleteAsync<T>(string collectionName, string id)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return false;

            var filter = Builders<T>.Filter.Eq("_id", id);
            var result = await collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Searches for documents in a collection by string name
        /// </summary>
        public async Task<List<T>> SearchAsync<T>(string collectionName, string searchField, string searchTerm)
        {
            var collection = GetCollection<T>(collectionName);
            if (collection == null) return new List<T>();

            var filter = Builders<T>.Filter.Regex(searchField, new BsonRegularExpression(searchTerm, "i"));
            return await collection.Find(filter).ToListAsync();
        }

        #endregion

        #region Helper Methods

        private string GetFieldName<T>(Expression<Func<T, object>> field)
        {
            if (field.Body is MemberExpression memberExpr)
            {
                return memberExpr.Member.Name;
            }
            else if (field.Body is UnaryExpression unaryExpr && unaryExpr.Operand is MemberExpression memberExpr2)
            {
                return memberExpr2.Member.Name;
            }
            
            throw new ArgumentException("Invalid field expression");
        }

        #endregion
    }
}