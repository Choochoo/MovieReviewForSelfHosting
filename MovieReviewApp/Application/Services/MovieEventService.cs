using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using Microsoft.Extensions.Logging;


namespace MovieReviewApp.Application.Services;

public class MovieEventService(MongoDbService databaseService, ILogger<MovieEventService> logger)
    : BaseService<MovieEvent>(databaseService, logger)
{
} 