using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Database;
using MovieReviewApp.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;

namespace MovieReviewApp.Infrastructure.FileSystem
{
    public class ImageService
    {
        private readonly IDatabaseService _database;
        private readonly IHttpClientFactory _httpClientFactory;
        private const int MaxWidth = 800;
        private const int MaxHeight = 1200;
        private const int Quality = 85;

        public ImageService(IDatabaseService database, IHttpClientFactory httpClientFactory)
        {
            _database = database;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<Guid?> SaveImageAsync(byte[] imageData, string fileName, string? originalUrl = null)
        {
            try
            {
                using var image = Image.Load(imageData);
                var optimizedImageData = await OptimizeImageAsync(image);
                var hash = ComputeHash(optimizedImageData);

                var existingImages = await _database.FindAsync<ImageStorage>(img => img.Hash == hash);
                var existingImage = existingImages.FirstOrDefault();

                if (existingImage != null)
                {
                    return existingImage.Id;
                }

                var imageStorage = new ImageStorage
                {
                    FileName = fileName,
                    ContentType = "image/jpeg",
                    ImageData = optimizedImageData,
                    Width = image.Width,
                    Height = image.Height,
                    FileSize = optimizedImageData.Length,
                    OriginalUrl = originalUrl,
                    Hash = hash
                };

                await _database.InsertAsync(imageStorage);
                return imageStorage.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving image: {ex.Message}");
                return null;
            }
        }

        public async Task<Guid?> SaveImageFromUrlAsync(string url)
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var imageData = await response.Content.ReadAsByteArrayAsync();
                var fileName = Path.GetFileName(new Uri(url).LocalPath) ?? "downloaded-image.jpg";

                return await SaveImageAsync(imageData, fileName, url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading image from URL: {ex.Message}");
                return null;
            }
        }

        public async Task<ImageStorage?> GetImageAsync(Guid imageId)
        {
            return await _database.GetByIdAsync<ImageStorage>(imageId);
        }

        public async Task<byte[]?> GetImageDataAsync(Guid imageId)
        {
            var image = await GetImageAsync(imageId);
            return image?.ImageData;
        }

        private async Task<byte[]> OptimizeImageAsync(Image image)
        {
            if (image.Width > MaxWidth || image.Height > MaxHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxWidth, MaxHeight),
                    Mode = ResizeMode.Max
                }));
            }

            using var memoryStream = new MemoryStream();
            var encoder = new JpegEncoder { Quality = Quality };
            await image.SaveAsync(memoryStream, encoder);
            return memoryStream.ToArray();
        }

        private static string ComputeHash(byte[] data)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(data);
            return Convert.ToBase64String(hash);
        }

        public async Task<bool> DeleteImageAsync(Guid imageId)
        {
            try
            {
                return await _database.DeleteByIdAsync<ImageStorage>(imageId);
            }
            catch
            {
                return false;
            }
        }
    }
}