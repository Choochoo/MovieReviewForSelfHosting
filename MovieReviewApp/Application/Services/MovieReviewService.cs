using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class MovieReviewService(IRepository<MovieEvent> repository, ILogger<MovieReviewService> logger)
    : BaseService<MovieEvent>(repository, logger)
{
}
