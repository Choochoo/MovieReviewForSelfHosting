using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Repositories
{
    public class ImageStorageRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<ImageStorageRepository> _logger;

        public ImageStorageRepository(
            IDatabaseService databaseService,
            ILogger<ImageStorageRepository> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<List<ImageStorage>> GetAllAsync()
        {
            try
            {
                return await _databaseService.GetAllAsync<ImageStorage>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all images");
                return new List<ImageStorage>();
            }
        }

        public async Task<ImageStorage?> GetByIdAsync(string id)
        {
            try
            {
                return await _databaseService.GetByIdAsync<ImageStorage>(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get image by id {Id}", id);
                return null;
            }
        }

        public async Task<ImageStorage> SaveAsync(byte[] imageData, string fileName, string contentType, int width, int height, string? originalUrl = null)
        {
            try
            {
                var image = new ImageStorage
                {
                    FileName = fileName,
                    ContentType = contentType,
                    ImageData = imageData,
                    Width = width,
                    Height = height,
                    FileSize = imageData.Length,
                    UploadDate = DateTime.UtcNow,
                    OriginalUrl = originalUrl
                };

                await _databaseService.InsertAsync(image);
                _logger.LogInformation("Saved image {FileName}", fileName);
                return image;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save image {FileName}", fileName);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            try
            {
                var image = await GetByIdAsync(id);
                if (image == null)
                    return false;

                await _databaseService.DeleteAsync<ImageStorage>(id);
                _logger.LogInformation("Deleted image {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete image {Id}", id);
                return false;
            }
        }

        public async Task<List<ImageStorage>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var images = await _databaseService.GetAllAsync<ImageStorage>();
                return images
                    .Where(i => i.UploadDate >= startDate && i.UploadDate <= endDate)
                    .OrderByDescending(i => i.UploadDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get images by date range");
                return new List<ImageStorage>();
            }
        }

        public async Task<List<ImageStorage>> GetByContentTypeAsync(string contentType)
        {
            try
            {
                var images = await _databaseService.GetAllAsync<ImageStorage>();
                return images
                    .Where(i => i.ContentType == contentType)
                    .OrderByDescending(i => i.UploadDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get images by content type {ContentType}", contentType);
                return new List<ImageStorage>();
            }
        }

        public async Task<ImageStorage?> GetByHashAsync(string hash)
        {
            try
            {
                var images = await _databaseService.GetAllAsync<ImageStorage>();
                return images.FirstOrDefault(i => i.Hash == hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get image by hash {Hash}", hash);
                return null;
            }
        }
    }
} 