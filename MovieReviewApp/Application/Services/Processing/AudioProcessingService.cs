using MovieReviewApp.Models;
using NAudio.Wave;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace MovieReviewApp.Application.Services.Processing;

public class AudioProcessingService
{
    private readonly ILogger<AudioProcessingService> _logger;
    private readonly IWebHostEnvironment _webHost;

    public AudioProcessingService(ILogger<AudioProcessingService> logger, IWebHostEnvironment webHost)
    {
        _logger = logger;
        _webHost = webHost;
    }

    public async Task<string?> GenerateAudioClipAsync(string sourceAudioPath, double startTimeSeconds, double endTimeSeconds, string sessionId, string clipId)
    {
        try
        {
            double duration = endTimeSeconds - startTimeSeconds;
            if (duration <= 0 || duration > 300) // Max 5 minutes
            {
                _logger.LogWarning("Invalid clip duration: {Duration} seconds", duration);
                return null;
            }

            // Create clips directory
            string clipsDir = Path.Combine(_webHost.WebRootPath, "clips", sessionId);
            Directory.CreateDirectory(clipsDir);

            string outputPath = Path.Combine(clipsDir, $"{clipId}.mp3");
            string tempWavPath = Path.Combine(clipsDir, $"{clipId}_temp.wav");

            try
            {
                // Extract audio segment using NAudio
                await ExtractAudioSegmentAsync(sourceAudioPath, tempWavPath, startTimeSeconds, endTimeSeconds);

                // Convert to MP3 if needed (or keep as WAV if MP3 conversion is complex)
                if (File.Exists(tempWavPath))
                {
                    // For now, just rename the WAV to MP3 extension (browser can play WAV)
                    // In future, could add MP3 encoding here
                    string finalOutputPath = Path.ChangeExtension(outputPath, ".wav");
                    File.Move(tempWavPath, finalOutputPath);

                    // Return relative URL path
                    return $"/clips/{sessionId}/{Path.GetFileName(finalOutputPath)}";
                }
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempWavPath))
                {
                    try { File.Delete(tempWavPath); } catch { }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate audio clip from {SourcePath} ({Start}-{End})",
                sourceAudioPath, startTimeSeconds, endTimeSeconds);
            return null;
        }
    }

    private async Task ExtractAudioSegmentAsync(string inputPath, string outputPath, double startSeconds, double endSeconds)
    {
        await Task.Run(() =>
        {
            using AudioFileReader reader = new AudioFileReader(inputPath);

            long startPosition = (long)(startSeconds * reader.WaveFormat.SampleRate * reader.WaveFormat.Channels * (reader.WaveFormat.BitsPerSample / 8));
            long endPosition = (long)(endSeconds * reader.WaveFormat.SampleRate * reader.WaveFormat.Channels * (reader.WaveFormat.BitsPerSample / 8));

            // Ensure positions are within bounds
            startPosition = Math.Max(0, Math.Min(startPosition, reader.Length));
            endPosition = Math.Max(startPosition, Math.Min(endPosition, reader.Length));

            long length = endPosition - startPosition;

            reader.Position = startPosition;

            using WaveFileWriter writer = new WaveFileWriter(outputPath, reader.WaveFormat);

            byte[] buffer = new byte[reader.WaveFormat.AverageBytesPerSecond]; // 1 second buffer
            long totalBytesRead = 0;

            while (totalBytesRead < length)
            {
                int bytesToRead = (int)Math.Min(buffer.Length, length - totalBytesRead);
                int bytesRead = reader.Read(buffer, 0, bytesToRead);

                if (bytesRead == 0) break;

                writer.Write(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;
            }
        });
    }

    public async Task<List<string>> GenerateClipsForTopFiveAsync(MovieSession session, TopFiveList topFive)
    {
        List<string> clipUrls = new List<string>();

        for (int i = 0; i < topFive.Entries.Count; i++)
        {
            TopFiveEntry entry = topFive.Entries[i];

            // Find the source audio file
            AudioFile? sourceFile = session.AudioFiles.FirstOrDefault(f => f.FileName == entry.SourceAudioFile);
            if (sourceFile == null)
            {
                _logger.LogWarning("Source audio file not found: {FileName}", entry.SourceAudioFile);
                continue;
            }

            string clipId = $"rank{entry.Rank}_{Guid.NewGuid():N}";

            // Only generate clip if both start and end times are present
            if (entry.StartTimeSeconds.HasValue && entry.EndTimeSeconds.HasValue)
            {
                // Add some padding around the timestamp (2 seconds before, 3 seconds after)
                double startTime = Math.Max(0, entry.StartTimeSeconds.Value - 2);
                double endTime = entry.EndTimeSeconds.Value + 3;

                string? clipUrl = await GenerateAudioClipAsync(sourceFile.FilePath, startTime, endTime, session.Id.ToString(), clipId);

                if (!string.IsNullOrEmpty(clipUrl))
                {
                    entry.AudioClipUrl = clipUrl;
                    clipUrls.Add(clipUrl);
                }
            }
            else
            {
                _logger.LogWarning("Start or end time is missing for entry rank {Rank} in TopFiveList.", entry.Rank);
            }
        }

        return clipUrls;
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

    public void CleanupOldClips(int daysOld = 30)
    {
        try
        {
            string clipsDir = Path.Combine(_webHost.WebRootPath, "clips");
            if (!Directory.Exists(clipsDir)) return;

            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);

            foreach (var sessionDir in Directory.GetDirectories(clipsDir))
            {
                var dirInfo = new DirectoryInfo(sessionDir);
                if (dirInfo.CreationTimeUtc < cutoffDate)
                {
                    try
                    {
                        Directory.Delete(sessionDir, true);
                        _logger.LogInformation("Cleaned up old clips directory: {Dir}", sessionDir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old clips directory: {Dir}", sessionDir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old clips");
        }
    }
} 