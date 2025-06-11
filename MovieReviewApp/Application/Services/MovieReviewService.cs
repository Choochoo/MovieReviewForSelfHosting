using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class MovieReviewService(MongoDbService databaseService, ILogger<MovieReviewService> logger)
    : BaseService<MovieEvent>(databaseService, logger)
{
}
