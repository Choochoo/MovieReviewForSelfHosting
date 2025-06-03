using System;

namespace MovieReviewApp.Attributes
{
    /// <summary>
    /// Specifies the MongoDB collection name for a class.
    /// If not specified, the collection name will be the class name + "s"
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class MongoCollectionAttribute : Attribute
    {
        public string CollectionName { get; }

        public MongoCollectionAttribute(string collectionName)
        {
            CollectionName = collectionName;
        }
    }
}