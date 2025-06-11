using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;
using MovieReviewApp.Attributes;
using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Infrastructure.Database
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
                    MongoUrlBuilder connectionBuilder = new MongoUrlBuilder(mongoConnection);
                    string instanceDbName = $"{connectionBuilder.DatabaseName ?? "moviereview"}_{instanceManager.InstanceName.ToLower().Replace("-", "_")}";
                    connectionBuilder.DatabaseName = instanceDbName;

                    string finalConnectionString = connectionBuilder.ToMongoUrl().ToString();
                    MongoClient client = new MongoClient(finalConnectionString);
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
            Type type = typeof(T);

            return _collectionNameCache.GetOrAdd(type, t =>
            {
                // Check for MongoCollection attribute
                MongoCollectionAttribute? attribute = t.GetCustomAttribute<MongoCollectionAttribute>();
                if (attribute != null)
                {
                    return attribute.CollectionName;
                }

                // Use convention: ClassName -> ClassNames (with proper pluralization)
                string name = t.Name;

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
        public IMongoCollection<T>? GetCollection<T>()
        {
            if (_database == null) return null;

            string collectionName = GetCollectionName<T>();
            return _database.GetCollection<T>(collectionName);
        }

        #region CRUD Operations - Type-based only!

        /// <summary>
        /// Gets all documents of type T
        /// </summary>
        public async Task<List<T>> GetAllAsync<T>() where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return new List<T>();

            return await collection.Find(_ => true).ToListAsync();
        }

        /// <summary>
        /// Gets a document by ID (supports both string and Guid)
        /// </summary>
        public async Task<T?> GetByIdAsync<T>(Guid id) where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            FilterDefinition<T> filter = Builders<T>.Filter.Eq("_id", id);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Finds documents matching a filter expression
        /// </summary>
        public async Task<List<T>> FindAsync<T>(Expression<Func<T, bool>> filter) where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return new List<T>();

            return await collection.Find(filter).ToListAsync();
        }

        /// <summary>
        /// Finds documents matching a filter definition with optional sorting and limit
        /// </summary>
        public async Task<List<T>> FindAsync<T>(
            FilterDefinition<T> filter,
            SortDefinition<T>? sort = null,
            int? limit = null) where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return new List<T>();

            IFindFluent<T, T> query = collection.Find(filter);

            if (sort != null)
                query = query.Sort(sort);

            if (limit.HasValue)
                query = query.Limit(limit.Value);

            return await query.ToListAsync();
        }

        /// <summary>
        /// Finds a single document matching a filter expression
        /// </summary>
        public async Task<T?> FindOneAsync<T>(Expression<Func<T, bool>> filter) where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return default;

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Inserts a new document
        /// </summary>
        public async Task InsertAsync<T>(T entity)
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null)
                throw new InvalidOperationException("Database not connected");

            await collection.InsertOneAsync(entity);
        }

        /// <summary>
        /// Inserts multiple documents
        /// </summary>
        public async Task InsertManyAsync<T>(IEnumerable<T> documents)
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null)
                throw new InvalidOperationException("Database not connected");

            await collection.InsertManyAsync(documents);
        }

        /// <summary>
        /// Updates or inserts a document based on its ID
        /// </summary>
        public async Task UpsertAsync<T>(T entity)
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null)
                throw new InvalidOperationException("Database not connected");

            // Get ID value using reflection
            PropertyInfo? idProperty = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("_id");
            if (idProperty == null)
                throw new InvalidOperationException($"Type {typeof(T).Name} must have an Id or _id property");

            object? idValue = idProperty.GetValue(entity);
            if (idValue == null)
            {
                // If no ID, just insert
                await collection.InsertOneAsync(entity);
                return;
            }

            // Create filter with proper type handling for GUIDs
            FilterDefinition<T> filter;
            if (idValue is Guid guidId)
            {
                // Use strongly typed filter for GUIDs to preserve serialization settings
                filter = Builders<T>.Filter.Eq("_id", guidId);
            }
            else
            {
                filter = Builders<T>.Filter.Eq("_id", idValue);
            }

            ReplaceOptions options = new ReplaceOptions { IsUpsert = true };
            _ = await collection.ReplaceOneAsync(filter, entity, options);
        }

        /// <summary>
        /// Updates a single document matching the filter
        /// </summary>
        public async Task<bool> UpdateOneAsync<T>(
            Expression<Func<T, bool>> filter,
            UpdateDefinition<T> update)
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return false;

            UpdateResult result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Updates multiple documents matching the filter
        /// </summary>
        public async Task<long> UpdateManyAsync<T>(
            Expression<Func<T, bool>> filter,
            UpdateDefinition<T> update)
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return 0;

            UpdateResult result = await collection.UpdateManyAsync(filter, update);
            return result.ModifiedCount;
        }

        /// <summary>
        /// Deletes a document by ID
        /// </summary>
        public async Task<bool> DeleteByIdAsync<T>(Guid id) where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return false;

            FilterDefinition<T> filter = Builders<T>.Filter.Eq("_id", id);
            DeleteResult result = await collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }

        /// <summary>
        /// Deletes a document by ID (generic version for compatibility)
        /// </summary>
        public async Task<bool> DeleteAsync<T>(Guid id) where T : class
        {
            return await DeleteByIdAsync<T>(id);
        }

        /// <summary>
        /// Deletes a document by ID (string version for compatibility)
        /// </summary>
        public async Task DeleteAsync<T>(string id) where T : class
        {
            if (!Guid.TryParse(id, out Guid guidId))
            {
                return;
            }
            _ = await DeleteByIdAsync<T>(guidId);
        }

        /// <summary>
        /// Deletes documents matching a filter
        /// </summary>
        public async Task<long> DeleteManyAsync<T>(Expression<Func<T, bool>> filter)
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return 0;

            DeleteResult result = await collection.DeleteManyAsync(filter);
            return result.DeletedCount;
        }

        /// <summary>
        /// Counts documents matching a filter
        /// </summary>
        public async Task<long> CountAsync<T>(Expression<Func<T, bool>>? filter = null) where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
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
        public async Task<bool> AnyAsync<T>(Expression<Func<T, bool>>? filter = null) where T : class
        {
            long count = await CountAsync(filter);
            return count > 0;
        }

        /// <summary>
        /// Performs a text search on specified fields
        /// </summary>
        public async Task<List<T>> SearchTextAsync<T>(string searchTerm, params Expression<Func<T, object>>[] searchFields) where T : class
        {
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return new List<T>();

            List<FilterDefinition<T>> filters = new List<FilterDefinition<T>>();

            foreach (Expression<Func<T, object>> field in searchFields)
            {
                string fieldName = GetFieldName(field);
                FilterDefinition<T> filter = Builders<T>.Filter.Regex(fieldName, new BsonRegularExpression(searchTerm, "i"));
                filters.Add(filter);
            }

            FilterDefinition<T> combinedFilter = filters.Any()
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
            IMongoCollection<T>? collection = GetCollection<T>();
            if (collection == null) return (new List<T>(), 0);

            IFindFluent<T, T> query = filter == null
                ? collection.Find(_ => true)
                : collection.Find(filter);

            long totalCount = await query.CountDocumentsAsync();

            if (orderBy != null)
            {
                query = descending
                    ? query.SortByDescending(orderBy)
                    : query.SortBy(orderBy);
            }

            List<T> items = await query
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return (items, totalCount);
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
