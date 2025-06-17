namespace MovieReviewApp.Infrastructure.Constants;

/// <summary>
/// Constants for Gladia API service operations
/// </summary>
public static class GladiaConstants
{
    /// <summary>
    /// Maximum number of retry attempts for upload operations
    /// </summary>
    public const int MaxRetries = 3;

    /// <summary>
    /// Base delay in milliseconds for exponential backoff retry strategy
    /// </summary>
    public const int BaseDelayMs = 2000;

    /// <summary>
    /// Default timeout for Gladia API requests in minutes
    /// </summary>
    public const int DefaultTimeoutMinutes = 10;

    /// <summary>
    /// File stream buffer size for audio file uploads (80KB)
    /// </summary>
    public const int FileStreamBufferSize = 81920;

    /// <summary>
    /// Large file threshold in bytes (100MB) for automatic conversion
    /// </summary>
    public const long LargeFileThresholdBytes = 104_857_600; // 100 MB

    /// <summary>
    /// Polling interval in milliseconds for checking transcription status
    /// </summary>
    public const int PollingIntervalMs = 5000;
}