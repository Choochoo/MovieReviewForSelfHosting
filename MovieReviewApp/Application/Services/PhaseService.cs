using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class PhaseService(MongoDbService databaseService, ILogger<PhaseService> logger)
    : BaseService<Phase>(databaseService, logger)
{
}
