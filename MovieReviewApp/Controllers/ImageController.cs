using Microsoft.AspNetCore.Mvc;
using MovieReviewApp.Infrastructure.FileSystem;

namespace MovieReviewApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImageController : ControllerBase
    {
        private readonly ImageService _imageService;

        public ImageController(ImageService imageService)
        {
            _imageService = imageService;
        }

        [HttpGet("{imageId}")]
        public async Task<IActionResult> GetImage(Guid imageId)
        {
            Models.ImageStorage? image = await _imageService.GetImageAsync(imageId);
            if (image == null)
            {
                return NotFound();
            }

            return File(image.ImageData, image.ContentType);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file provided");
            }

            if (!file.ContentType.StartsWith("image/"))
            {
                return BadRequest("File must be an image");
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            byte[] imageData = memoryStream.ToArray();

            Guid? imageId = await _imageService.SaveImageAsync(imageData, file.FileName);
            if (imageId == null)
            {
                return BadRequest("Failed to save image");
            }

            return Ok(new { imageId });
        }

        [HttpPost("upload-from-url")]
        public async Task<IActionResult> UploadFromUrl([FromBody] UrlUploadRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                return BadRequest("URL is required");
            }

            Guid? imageId = await _imageService.SaveImageFromUrlAsync(request.Url);
            if (imageId == null)
            {
                return BadRequest("Failed to download and save image from URL");
            }

            return Ok(new { imageId });
        }
    }

    public class UrlUploadRequest
    {
        public string Url { get; set; } = string.Empty;
    }
}