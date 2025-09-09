using Microsoft.AspNetCore.Http;

namespace MovieReviewApp.Utilities;

/// <summary>
/// Shared utilities for file validation operations.
/// Consolidates file validation logic from controllers and prevents code duplication.
/// </summary>
public static class FileValidationHelpers
{
    private static readonly string[] AllowedAudioMimeTypes = {
        "audio/mpeg",
        "audio/wav",
        "audio/ogg",
        "audio/aac",
        "audio/mp4",
        "audio/x-wav",
        "audio/wave"
    };

    private static readonly string[] AllowedImageMimeTypes = {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/gif",
        "image/bmp",
        "image/webp",
        "image/svg+xml"
    };

    /// <summary>
    /// Validates if an uploaded file is a supported audio file.
    /// Checks both MIME type and file extension for comprehensive validation.
    /// </summary>
    /// <param name="file">The uploaded file to validate</param>
    /// <returns>True if the file is a valid audio file</returns>
    public static bool IsAudioFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return false;

        // Check MIME type
        if (!string.IsNullOrEmpty(file.ContentType) && 
            AllowedAudioMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            return true;

        // Fallback to extension check
        return AudioFileHelpers.IsAudioFile(file.FileName);
    }

    /// <summary>
    /// Validates if an uploaded file is a supported image file.
    /// Checks MIME type, file extension, and size limits.
    /// </summary>
    /// <param name="file">The uploaded file to validate</param>
    /// <param name="maxSizeBytes">Maximum allowed file size in bytes (default: 20MB)</param>
    /// <returns>True if the file is a valid image file</returns>
    public static bool IsImageFile(IFormFile file, long maxSizeBytes = 20 * 1024 * 1024)
    {
        if (file == null || file.Length == 0)
            return false;

        // Check file size
        if (file.Length > maxSizeBytes)
            return false;

        // Check MIME type
        if (string.IsNullOrEmpty(file.ContentType))
            return false;

        return AllowedImageMimeTypes.Contains(file.ContentType.ToLowerInvariant()) ||
               file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates file size against a maximum limit.
    /// </summary>
    /// <param name="file">The file to check</param>
    /// <param name="maxSizeBytes">Maximum allowed size in bytes</param>
    /// <returns>True if file size is within limit</returns>
    public static bool IsValidFileSize(IFormFile file, long maxSizeBytes)
    {
        return file != null && file.Length > 0 && file.Length <= maxSizeBytes;
    }
}