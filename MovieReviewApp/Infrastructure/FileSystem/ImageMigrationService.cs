using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Database;
using MovieReviewApp.Enums;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.FileSystem
{
    public class ImageMigrationService
    {
        private readonly IDatabaseService _database;
        private readonly ImageService _imageService;
        private readonly ILogger<ImageMigrationService> _logger;

        public ImageMigrationService(IDatabaseService database, ImageService imageService, ILogger<ImageMigrationService> logger)
        {
            _database = database;
            _imageService = imageService;
            _logger = logger;
        }

        public async Task<int> MigrateExistingUrlsToBlobs()
        {
            _logger.LogInformation("Starting migration of existing image URLs to blob storage...");
            
            var movieEvents = await _database.GetAllAsync<MovieEvent>();
            var migratedCount = 0;
            var failedCount = 0;

            foreach (var movieEvent in movieEvents)
            {
                if (!string.IsNullOrEmpty(movieEvent.PosterUrl) && !movieEvent.ImageId.HasValue)
                {
                    try
                    {
                        _logger.LogInformation($"Migrating poster URL for movie: {movieEvent.Movie} - {movieEvent.PosterUrl}");
                        
                        var imageId = await _imageService.SaveImageFromUrlAsync(movieEvent.PosterUrl);
                        
                        if (imageId.HasValue)
                        {
                            movieEvent.ImageId = imageId;
                            movieEvent.PosterUrl = null; // Clear the URL after successful migration
                            await _database.UpsertAsync(movieEvent);
                            
                            migratedCount++;
                            _logger.LogInformation($"Successfully migrated poster for: {movieEvent.Movie}");
                        }
                        else
                        {
                            failedCount++;
                            _logger.LogWarning($"Failed to download and save image for: {movieEvent.Movie} from URL: {movieEvent.PosterUrl}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.LogError(ex, $"Error migrating poster for movie: {movieEvent.Movie} from URL: {movieEvent.PosterUrl}");
                    }
                }
            }

            _logger.LogInformation($"Migration completed. Migrated: {migratedCount}, Failed: {failedCount}");
            return migratedCount;
        }


        public async Task<(int totalMovies, int withImageIds, int withUrls, int withBoth, int withNeither)> GetMigrationStatus()
        {
            var movieEvents = await _database.GetAllAsync<MovieEvent>();
            
            var totalMovies = movieEvents.Count();
            var withImageIds = movieEvents.Count(me => me.ImageId.HasValue);
            var withUrls = movieEvents.Count(me => !string.IsNullOrEmpty(me.PosterUrl));
            var withBoth = movieEvents.Count(me => me.ImageId.HasValue && !string.IsNullOrEmpty(me.PosterUrl));
            var withNeither = movieEvents.Count(me => !me.ImageId.HasValue && string.IsNullOrEmpty(me.PosterUrl));

            return (totalMovies, withImageIds, withUrls, withBoth, withNeither);
        }
    }
}