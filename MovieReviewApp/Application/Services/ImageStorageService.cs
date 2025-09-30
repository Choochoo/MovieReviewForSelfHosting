using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class ImageStorageService(IRepository<ImageStorage> repository, ILogger<ImageStorageService> logger)
    : BaseService<ImageStorage>(repository, logger)
{
} 