using MovieReviewApp.Models;
using NAudio.Wave;

namespace MovieReviewApp.Application.Services;

public class AudioClipService
{
    private readonly ILogger<AudioClipService> _logger;
    private readonly IWebHostEnvironment _webHost;

    public AudioClipService(ILogger<AudioClipService> logger, IWebHostEnvironment webHost)
    {
        _logger = logger;
        _webHost = webHost;
    }


    public TimeSpan ParseTimestamp(string timestamp)
    {
        try
        {
            // Handle formats like "1:23", "12:34", "1:23:45"
            string[] parts = timestamp.Split(':');

            return parts.Length switch
            {
                2 => new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1])),
                3 => new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2])),
                _ => TimeSpan.Zero
            };
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

}