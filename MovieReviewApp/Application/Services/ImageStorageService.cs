using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using Microsoft.Extensions.Logging;


namespace MovieReviewApp.Application.Services;

public class ImageStorageService(MongoDbService databaseService, ILogger<ImageStorageService> logger)
    : BaseService<ImageStorage>(databaseService, logger)
{
} 