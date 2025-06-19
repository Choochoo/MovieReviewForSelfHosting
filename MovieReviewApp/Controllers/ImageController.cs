using Microsoft.AspNetCore.Mvc;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Models;

namespace MovieReviewApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImageController : ControllerBase
    {
        private readonly ImageService _imageService;
        private const int MaxFileSize = 20 * 1024 * 1024; // 20MB

        /// <summary>
        /// Initializes a new instance of the ImageController class.
        /// </summary>
        /// <param name="imageService">The image service.</param>
        public ImageController(ImageService imageService)
        {
            _imageService = imageService;
        }

        /// <summary>
        /// Retrieves an image by its ID.
        /// </summary>
        /// <param name="imageId">The ID of the image to retrieve.</param>
        /// <returns>The image file with appropriate content type.</returns>
        [HttpGet("{imageId}")]
        public async Task<IActionResult> GetImage(Guid imageId)
        {
            ImageStorage? image = await _imageService.GetImageAsync(imageId);
            if (image == null)
            {
                return NotFound();
            }

            return File(image.ImageData, image.ContentType);
        }

        /// <summary>
        /// Uploads an image file.
        /// </summary>
        /// <param name="file">The image file to upload.</param>
        /// <returns>The ID of the uploaded image.</returns>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile file)
        {
            if (!IsValidImageFile(file))
            {
                return BadRequest("Invalid image file");
            }

            Guid? imageId = await SaveImageFromFile(file);
            if (!imageId.HasValue)
            {
                return BadRequest("Failed to save image");
            }

            return Ok(new { imageId });
        }

        /// <summary>
        /// Uploads an image from a URL.
        /// </summary>
        /// <param name="request">The request containing the URL to download from.</param>
        /// <returns>The ID of the uploaded image.</returns>
        [HttpPost("upload-from-url")]
        public async Task<IActionResult> UploadFromUrl([FromBody] UrlUploadRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return BadRequest("URL is required");
            }

            Guid? imageId = await _imageService.SaveImageFromUrlAsync(request.Url);
            if (!imageId.HasValue)
            {
                return BadRequest("Failed to download and save image from URL");
            }

            return Ok(new { imageId });
        }

        private bool IsValidImageFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return false;
            }

            if (!file.ContentType.StartsWith("image/"))
            {
                return false;
            }

            if (file.Length > MaxFileSize)
            {
                return false;
            }

            return true;
        }

        private async Task<Guid?> SaveImageFromFile(IFormFile file)
        {
            try
            {
                using MemoryStream memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                byte[] imageData = memoryStream.ToArray();

                return await _imageService.SaveImageAsync(imageData, file.FileName);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public class UrlUploadRequest
    {
        public string Url { get; set; } = string.Empty;
    }
}