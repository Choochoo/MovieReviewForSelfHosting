using System.Text;
using System.Text.Json;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services;

public class GladiaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GladiaService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl = "https://api.gladia.io";

    public GladiaService(HttpClient httpClient, IConfiguration configuration, ILogger<GladiaService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _apiKey = _configuration["Gladia:ApiKey"] ?? throw new InvalidOperationException("Gladia API key not found in configuration");
        
        _httpClient.DefaultRequestHeaders.Add("x-gladia-key", _apiKey);
    }

    public async Task<string> UploadFileAsync(string filePath)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(filePath);
            using var fileContent = new StreamContent(fileStream);
            
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "audio", Path.GetFileName(filePath));

            var response = await _httpClient.PostAsync($"{_baseUrl}/v2/upload", form);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var uploadResult = JsonSerializer.Deserialize<UploadResponse>(jsonResponse);
            
            return uploadResult?.audio_url ?? throw new Exception("Failed to get upload URL from response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FilePath} to Gladia", filePath);
            throw;
        }
    }

    public async Task<string> StartTranscriptionAsync(string audioUrl, bool enableSpeakerDiarization = true)
    {
        try
        {
            var request = new TranscriptionRequest
            {
                audio_url = audioUrl,
                diarization = enableSpeakerDiarization,
                diarization_config = new DiarizationConfig
                {
                    number_of_speakers = 6,
                    min_speakers = 1,
                    max_speakers = 6
                },
                summarization = true,
                sentiment_analysis = true,
                named_entity_recognition = true,
                chapterization = true
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/v2/pre-recorded", content);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var transcriptionResult = JsonSerializer.Deserialize<TranscriptionResponse>(jsonResponse);
            
            return transcriptionResult?.id ?? throw new Exception("Failed to get transcription ID from response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start transcription for audio URL {AudioUrl}", audioUrl);
            throw;
        }
    }

    public async Task<TranscriptionResult> GetTranscriptionResultAsync(string transcriptionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/v2/pre-recorded/{transcriptionId}");
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TranscriptionResult>(jsonResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            
            return result ?? throw new Exception("Failed to deserialize transcription result");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transcription result for ID {TranscriptionId}", transcriptionId);
            throw;
        }
    }

    public async Task<TranscriptionResult> WaitForTranscriptionAsync(string transcriptionId, int maxWaitTimeMinutes = 30)
    {
        var maxWaitTime = TimeSpan.FromMinutes(maxWaitTimeMinutes);
        var startTime = DateTime.UtcNow;
        var checkInterval = TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            var result = await GetTranscriptionResultAsync(transcriptionId);
            
            if (result.status == "done")
            {
                return result;
            }
            else if (result.status == "error")
            {
                throw new Exception($"Transcription failed: {result.error?.message ?? "Unknown error"}");
            }

            _logger.LogInformation("Transcription {TranscriptionId} status: {Status}. Waiting...", transcriptionId, result.status);
            await Task.Delay(checkInterval);
        }

        throw new TimeoutException($"Transcription {transcriptionId} did not complete within {maxWaitTimeMinutes} minutes");
    }

    public async Task<List<TranscriptionResult>> ProcessMultipleFilesAsync(List<string> filePaths, 
        Action<string, int, int>? progressCallback = null)
    {
        var results = new List<TranscriptionResult>();
        var totalFiles = filePaths.Count;

        for (int i = 0; i < filePaths.Count; i++)
        {
            var filePath = filePaths[i];
            progressCallback?.Invoke($"Processing {Path.GetFileName(filePath)}", i + 1, totalFiles);

            try
            {
                // Upload file
                var audioUrl = await UploadFileAsync(filePath);
                
                // Start transcription
                var transcriptionId = await StartTranscriptionAsync(audioUrl);
                
                // Wait for completion
                var result = await WaitForTranscriptionAsync(transcriptionId);
                result.source_file_path = filePath;
                
                results.Add(result);
                
                _logger.LogInformation("Successfully processed file {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file {FilePath}", filePath);
                
                // Add a failed result to maintain order
                results.Add(new TranscriptionResult
                {
                    status = "error",
                    source_file_path = filePath,
                    error = new TranscriptionError { message = ex.Message }
                });
            }
        }

        return results;
    }
}

// DTOs for Gladia API
public class UploadResponse
{
    public string? audio_url { get; set; }
}

public class TranscriptionRequest
{
    public string audio_url { get; set; } = string.Empty;
    public bool diarization { get; set; } = true;
    public DiarizationConfig? diarization_config { get; set; }
    public bool summarization { get; set; } = true;
    public bool sentiment_analysis { get; set; } = true;
    public bool named_entity_recognition { get; set; } = true;
    public bool chapterization { get; set; } = true;
}

public class DiarizationConfig
{
    public int number_of_speakers { get; set; } = 6;
    public int min_speakers { get; set; } = 1;
    public int max_speakers { get; set; } = 6;
}

public class TranscriptionResponse
{
    public string? id { get; set; }
}

public class TranscriptionResult
{
    public string id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public TranscriptionData? result { get; set; }
    public TranscriptionError? error { get; set; }
    public string? source_file_path { get; set; } // Added for tracking
}

public class TranscriptionData
{
    public TranscriptionOutput? transcription { get; set; }
    public List<Speaker>? speakers { get; set; }
    public Summary? summarization { get; set; }
    public List<Chapter>? chapters { get; set; }
}

public class TranscriptionOutput
{
    public string? full_transcript { get; set; }
    public List<Utterance>? utterances { get; set; }
}

public class Utterance
{
    public double start { get; set; }
    public double end { get; set; }
    public string text { get; set; } = string.Empty;
    public int speaker { get; set; }
    public double confidence { get; set; }
    public List<Word>? words { get; set; }
}

public class Word
{
    public string word { get; set; } = string.Empty;
    public double start { get; set; }
    public double end { get; set; }
    public double confidence { get; set; }
}

public class Speaker
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
}

public class Summary
{
    public string? summary { get; set; }
    public List<string>? key_points { get; set; }
}

public class Chapter
{
    public string? title { get; set; }
    public double start { get; set; }
    public double end { get; set; }
    public string? summary { get; set; }
}

public class TranscriptionError
{
    public string message { get; set; } = string.Empty;
    public string? code { get; set; }
}