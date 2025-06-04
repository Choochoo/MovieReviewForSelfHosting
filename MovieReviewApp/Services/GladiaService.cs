using System.Text;
using System.Text.Json;
using MovieReviewApp.Models;
using MovieReviewApp.Services;
using NAudio.Wave;

namespace MovieReviewApp.Services;

public class GladiaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GladiaService> _logger;
    private readonly SecretsManager _secretsManager;
    private readonly AudioFileOrganizer _audioOrganizer;
    private readonly string _apiKey;
    private readonly string _baseUrl = "https://api.gladia.io";

    public GladiaService(HttpClient httpClient, IConfiguration configuration, SecretsManager secretsManager, AudioFileOrganizer audioOrganizer, ILogger<GladiaService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _secretsManager = secretsManager;
        _audioOrganizer = audioOrganizer;
        _logger = logger;
        
        // Get API key from secrets manager directly
        _apiKey = _secretsManager.GetSecret("Gladia:ApiKey") ?? string.Empty;
        
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-gladia-key", _apiKey);
            _logger.LogInformation("Gladia service initialized with API key");
        }
        else
        {
            _logger.LogWarning("Gladia service initialized without API key - transcription will be disabled");
        }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public void LogConfigurationStatus()
    {
        var allConfigKeys = _configuration.AsEnumerable().Where(kvp => kvp.Key.Contains("Gladia", StringComparison.OrdinalIgnoreCase));
        var secretValue = _secretsManager.GetSecret("Gladia:ApiKey");
        
        _logger.LogInformation("Gladia configuration status:");
        _logger.LogInformation("  IsConfigured: {IsConfigured}", IsConfigured);
        _logger.LogInformation("  API Key present: {HasApiKey}", !string.IsNullOrEmpty(_apiKey));
        _logger.LogInformation("  API Key length: {KeyLength}", _apiKey?.Length ?? 0);
        _logger.LogInformation("  SecretsManager value length: {SecretLength}", secretValue?.Length ?? 0);
        _logger.LogInformation("  Configuration source:");
        
        foreach (var configItem in allConfigKeys)
        {
            var maskedValue = string.IsNullOrEmpty(configItem.Value) ? "[NULL]" : 
                             configItem.Value.Length > 10 ? $"{configItem.Value[..10]}..." : "[SHORT]";
            _logger.LogInformation("    Config[{Key}]: {Value}", configItem.Key, maskedValue);
        }
    }

    private bool IsFFmpegAvailable()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process();
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ConvertToMp3Async(string inputPath, string outputPath)
    {
        try
        {
            _logger.LogInformation("Converting {InputFile} to MP3", Path.GetFileName(inputPath));
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputPath}\" -codec:a libmp3lame -b:a 128k -ar 44100 -ac 2 \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process();
            process.StartInfo = startInfo;
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = errorBuilder.ToString();
                throw new Exception($"FFmpeg conversion failed (exit code {process.ExitCode}): {error}");
            }

            var originalSize = new FileInfo(inputPath).Length;
            var compressedSize = new FileInfo(outputPath).Length;
            var compressionRatio = (double)compressedSize / originalSize;
            
            _logger.LogInformation("Successfully converted {InputFile} to MP3. Size: {OriginalSize:N0} → {CompressedSize:N0} bytes ({CompressionRatio:P1})", 
                Path.GetFileName(inputPath), originalSize, compressedSize, compressionRatio);
            
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert {InputPath} to MP3", inputPath);
            throw;
        }
    }

    /// <summary>
    /// Phase 1: Convert all WAV files to MP3 format
    /// </summary>
    public async Task<List<(AudioFile audioFile, bool success, string? error)>> ConvertAllWavsToMp3Async(
        List<AudioFile> audioFiles, 
        string sessionFolderPath,
        Action<string, int, int>? progressCallback = null)
    {
        var results = new List<(AudioFile audioFile, bool success, string? error)>();
        var totalFiles = audioFiles.Count;
        
        _logger.LogInformation("Starting MP3 conversion for {TotalFiles} files", totalFiles);
        
        for (int i = 0; i < audioFiles.Count; i++)
        {
            var audioFile = audioFiles[i];
            progressCallback?.Invoke($"Converting {Path.GetFileName(audioFile.FilePath)}", i + 1, totalFiles);
            
            try
            {
                if (Path.GetExtension(audioFile.FilePath).ToLowerInvariant() != ".wav")
                {
                    // Already MP3 or other format, skip conversion
                    results.Add((audioFile, true, null));
                    continue;
                }
                
                if (!IsFFmpegAvailable())
                {
                    var error = "FFmpeg not available - required for WAV to MP3 conversion";
                    audioFile.ProcessingStatus = Models.AudioProcessingStatus.FailedMp3;
                    audioFile.ConversionError = error;
                    results.Add((audioFile, false, error));
                    continue;
                }
                
                // Get target folder using AudioFileOrganizer
                var targetStatus = Models.AudioProcessingStatus.PendingMp3;
                var mp3Folder = _audioOrganizer.GetFolderForStatus(targetStatus, sessionFolderPath);
                
                var mp3FileName = Path.ChangeExtension(Path.GetFileName(audioFile.FilePath), ".mp3");
                var mp3Path = Path.Combine(mp3Folder, mp3FileName);
                
                await ConvertToMp3Async(audioFile.FilePath, mp3Path);
                
                // Update audio file and move original to status folder
                audioFile.Mp3FilePath = mp3Path;
                audioFile.Mp3FileSize = new FileInfo(mp3Path).Length;
                audioFile.ConvertedAt = DateTime.UtcNow;
                audioFile.ProcessingStatus = targetStatus;
                
                // Move original WAV file to status folder with cleanup
                audioFile.FilePath = await _audioOrganizer.MoveFileToStatusFolder(audioFile, sessionFolderPath, cleanupSource: true);
                
                results.Add((audioFile, true, null));
                _logger.LogInformation("Converted {FileName} to MP3: {Mp3Path}", 
                    Path.GetFileName(audioFile.FilePath), mp3Path);
            }
            catch (Exception ex)
            {
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.Failed;
                audioFile.ConversionError = ex.Message;
                
                // Move failed file to failed folder
                audioFile.FilePath = await _audioOrganizer.MoveFileToStatusFolder(audioFile, sessionFolderPath);
                
                results.Add((audioFile, false, ex.Message));
                _logger.LogError(ex, "Failed to convert {FileName} to MP3", Path.GetFileName(audioFile.FilePath));
            }
        }
        
        return results;
    }

    /// <summary>
    /// Phase 2: Upload all MP3 files to Gladia for transcription
    /// </summary>
    public async Task<List<(AudioFile audioFile, bool success, string? error)>> UploadAllMp3sToGladiaAsync(
        List<AudioFile> audioFiles,
        string sessionFolderPath,
        Action<string, int, int>? progressCallback = null)
    {
        var results = new List<(AudioFile audioFile, bool success, string? error)>();
        var mp3Files = audioFiles.Where(f => !string.IsNullOrEmpty(f.Mp3FilePath) && 
                                           f.ProcessingStatus == Models.AudioProcessingStatus.PendingMp3).ToList();
        var totalFiles = mp3Files.Count;
        
        _logger.LogInformation("Starting Gladia upload for {TotalFiles} MP3 files", totalFiles);
        
        for (int i = 0; i < mp3Files.Count; i++)
        {
            var audioFile = mp3Files[i];
            progressCallback?.Invoke($"Uploading {Path.GetFileName(audioFile.Mp3FilePath!)}", i + 1, totalFiles);
            
            try
            {
                var audioUrl = await UploadSingleFileToGladiaAsync(audioFile.Mp3FilePath!);
                
                // Store the audio URL for the transcription phase
                audioFile.AudioUrl = audioUrl;
                audioFile.UploadedAt = DateTime.UtcNow;
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.UploadedToGladia;
                
                results.Add((audioFile, true, null));
                _logger.LogInformation("Uploaded {FileName} to Gladia: {AudioUrl}", 
                    Path.GetFileName(audioFile.Mp3FilePath!), audioUrl);
            }
            catch (Exception ex)
            {
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.FailedMp3;
                audioFile.ConversionError = ex.Message;
                
                // Move MP3 file to failed_mp3 folder
                if (!string.IsNullOrEmpty(audioFile.Mp3FilePath))
                {
                    audioFile.Mp3FilePath = await _audioOrganizer.MoveMp3FileToStatusFolder(audioFile, sessionFolderPath);
                }
                
                results.Add((audioFile, false, ex.Message));
                _logger.LogError(ex, "Failed to upload {FileName} to Gladia", Path.GetFileName(audioFile.Mp3FilePath!));
            }
        }
        
        return results;
    }

    /// <summary>
    /// Maps Gladia speaker labels to actual participant names based on file type
    /// </summary>
    public string MapSpeakerLabelsToNames(string transcriptText, Dictionary<int, string> micAssignments, string fileName)
    {
        if (string.IsNullOrEmpty(transcriptText) || !micAssignments.Any())
        {
            return transcriptText;
        }

        var result = transcriptText;
        var upperFileName = fileName.ToUpper();
        
        // Check if this is an individual mic file (MIC1.WAV, MIC2.WAV, etc.)
        var micMatch = System.Text.RegularExpressions.Regex.Match(upperFileName, @"^MIC(\d)\.WAV$");
        if (micMatch.Success)
        {
            var micNumber = int.Parse(micMatch.Groups[1].Value);
            if (micAssignments.TryGetValue(micNumber, out var participantName) && !string.IsNullOrEmpty(participantName))
            {
                // For individual mic files, replace ALL speaker labels with the mic owner's name
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\bSpeaker \d+:", $"{participantName}:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\bspeaker \d+:", $"{participantName}:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\[Speaker \d+\]:", $"[{participantName}]:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\(Speaker \d+\):", $"({participantName}):");
                
                _logger.LogInformation("Mapped all speakers in {FileName} to {ParticipantName}", fileName, participantName);
            }
        }
        // Check for other known individual files (PHONE.WAV, SOUND_PAD.WAV)
        else if (upperFileName == "PHONE.WAV")
        {
            // Replace all speakers with "Phone Input"
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\bSpeaker \d+:", "Phone Input:");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\bspeaker \d+:", "Phone Input:");
        }
        else if (upperFileName == "SOUND_PAD.WAV" || upperFileName == "SOUNDPAD.WAV")
        {
            // Replace all speakers with "Sound Effects"
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\bSpeaker \d+:", "Sound Effects:");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\bspeaker \d+:", "Sound Effects:");
        }
        // For master mix and other files, use traditional speaker number mapping
        else
        {
            // Map Speaker 1, Speaker 2, etc. to actual names based on mic assignments
            for (int i = 1; i <= 10; i++) // Support up to 10 speakers
            {
                if (micAssignments.TryGetValue(i, out var participantName) && !string.IsNullOrEmpty(participantName))
                {
                    // Replace "Speaker 1:" with "John:" etc.
                    result = result.Replace($"Speaker {i}:", $"{participantName}:");
                    result = result.Replace($"Speaker {i} :", $"{participantName}:");
                    result = result.Replace($"speaker {i}:", $"{participantName}:");
                    result = result.Replace($"speaker {i} :", $"{participantName}:");
                    
                    // Also handle cases where there might be brackets or other formatting
                    result = result.Replace($"[Speaker {i}]:", $"[{participantName}]:");
                    result = result.Replace($"(Speaker {i}):", $"({participantName}):");
                }
            }
            _logger.LogInformation("Applied speaker number mapping for {FileName}", fileName);
        }

        return result;
    }

    /// <summary>
    /// Upload a single file to Gladia and return the transcript ID
    /// </summary>
    private async Task<string> UploadSingleFileToGladiaAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Audio file not found: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        _logger.LogInformation("Uploading file {FileName} ({FileSize:N0} bytes) to Gladia", 
            Path.GetFileName(filePath), fileInfo.Length);

        using var form = new MultipartFormDataContent();
        
        // Create file stream with explicit buffer size for large files
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);
        
        // Create a byte array to ensure the stream is fully read before sending
        var fileBytes = new byte[fileInfo.Length];
        await fileStream.ReadAsync(fileBytes, 0, fileBytes.Length);
        
        using var fileContent = new ByteArrayContent(fileBytes);
        
        // Set appropriate content type based on file extension
        var contentType = extension switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg", 
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            _ => "audio/mpeg"
        };
        
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "audio", Path.GetFileName(filePath));
        
        // Add transcription configuration
        form.Add(new StringContent("true"), "enable_speaker_diarization");
        form.Add(new StringContent("true"), "enable_code_switching");
        form.Add(new StringContent("en"), "language");

        var response = await _httpClient.PostAsync("/upload", form);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gladia upload failed with status {response.StatusCode}: {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var uploadResult = JsonSerializer.Deserialize<UploadResponse>(content);
        
        if (uploadResult?.audio_url == null)
        {
            throw new Exception($"Gladia upload response missing audio_url: {content}");
        }

        _logger.LogInformation("Successfully uploaded {FileName} to Gladia with URL: {AudioUrl}", 
            Path.GetFileName(filePath), uploadResult.audio_url);
            
        return uploadResult.audio_url;
    }

    public async Task<string> UploadFileAsync(string filePath, AudioFile? audioFile = null)
    {
        string? tempMp3Path = null;
        
        try
        {
            // Validate file exists and is accessible
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Audio file not found: {filePath}");
            }

            var fileInfo = new FileInfo(filePath);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            // Determine if we should convert to MP3 for better upload performance
            const long LARGE_FILE_THRESHOLD = 100 * 1024 * 1024; // 100MB
            var shouldConvert = fileInfo.Length > LARGE_FILE_THRESHOLD && extension == ".wav";
            
            string actualFilePath = filePath;
            
            if (shouldConvert)
            {
                if (!IsFFmpegAvailable())
                {
                    var errorMessage = $"File {Path.GetFileName(filePath)} is too large ({fileInfo.Length:N0} bytes > {LARGE_FILE_THRESHOLD:N0}) and requires FFmpeg for MP3 conversion. Please install FFmpeg or use smaller files.";
                    _logger.LogError(errorMessage);
                    
                    if (audioFile != null)
                    {
                        audioFile.ProcessingStatus = Models.AudioProcessingStatus.FailedMp3;
                        audioFile.ConversionError = "FFmpeg not available - required for large file conversion";
                    }
                    
                    throw new InvalidOperationException(errorMessage);
                }
                
                _logger.LogInformation("File {FileName} is {FileSize:N0} bytes (>{Threshold:N0}), converting to MP3 for faster upload", 
                    Path.GetFileName(filePath), fileInfo.Length, LARGE_FILE_THRESHOLD);
                
                // Update audio file status if provided
                if (audioFile != null)
                {
                    audioFile.ProcessingStatus = Models.AudioProcessingStatus.PendingMp3;
                }
                
                try
                {
                    // Create temporary MP3 file
                    tempMp3Path = Path.ChangeExtension(Path.GetTempFileName(), ".mp3");
                    actualFilePath = await ConvertToMp3Async(filePath, tempMp3Path);
                    extension = ".mp3";
                    
                    // Update audio file with MP3 details if provided
                    if (audioFile != null)
                    {
                        audioFile.ProcessingStatus = Models.AudioProcessingStatus.ProcessedMp3;
                        audioFile.Mp3FilePath = actualFilePath;
                        audioFile.Mp3FileSize = new FileInfo(actualFilePath).Length;
                        audioFile.ConvertedAt = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FFmpeg conversion failed for {FileName}. Cannot upload large files without MP3 conversion.", Path.GetFileName(filePath));
                    
                    // Update audio file with error if provided
                    if (audioFile != null)
                    {
                        audioFile.ProcessingStatus = Models.AudioProcessingStatus.FailedMp3;
                        audioFile.ConversionError = ex.Message;
                    }
                    
                    throw new InvalidOperationException($"Failed to convert {Path.GetFileName(filePath)} to MP3. Large WAV files require successful MP3 conversion.", ex);
                }
            }
            else
            {
                _logger.LogInformation("Uploading file {FileName} ({FileSize:N0} bytes) to Gladia", 
                    Path.GetFileName(filePath), fileInfo.Length);
            }

            using var form = new MultipartFormDataContent();
            
            // Create file stream with explicit buffer size for large files
            using var fileStream = new FileStream(actualFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);
            
            // Create a byte array to ensure the stream is fully read before sending
            var actualFileInfo = new FileInfo(actualFilePath);
            var fileBytes = new byte[actualFileInfo.Length];
            await fileStream.ReadAsync(fileBytes, 0, fileBytes.Length);
            
            using var fileContent = new ByteArrayContent(fileBytes);
            
            // Set appropriate content type based on file extension
            var contentType = extension switch
            {
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".aac" => "audio/aac",
                ".ogg" => "audio/ogg",
                ".flac" => "audio/flac",
                _ => "audio/wav" // Default fallback
            };
            
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "audio", Path.GetFileName(actualFilePath));

            var response = await _httpClient.PostAsync($"{_baseUrl}/v2/upload", form);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gladia upload failed with status {StatusCode}: {ErrorContent}", 
                    response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var uploadResult = JsonSerializer.Deserialize<UploadResponse>(jsonResponse);
            
            var audioUrl = uploadResult?.audio_url ?? throw new Exception("Failed to get upload URL from response");
            _logger.LogInformation("Successfully uploaded {FileName} to Gladia, got URL: {AudioUrl}", 
                Path.GetFileName(actualFilePath), audioUrl);
            
            // Update audio file status if provided
            if (audioFile != null)
            {
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.UploadedToGladia;
                audioFile.UploadedAt = DateTime.UtcNow;
            }
            
            return audioUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FilePath} to Gladia", filePath);
            throw;
        }
        finally
        {
            // Clean up temporary MP3 file
            if (!string.IsNullOrEmpty(tempMp3Path) && File.Exists(tempMp3Path))
            {
                try
                {
                    File.Delete(tempMp3Path);
                    _logger.LogDebug("Cleaned up temporary MP3 file: {TempPath}", tempMp3Path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary MP3 file: {TempPath}", tempMp3Path);
                }
            }
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

    public async Task<List<TranscriptionResult>> ProcessMultipleFilesAsync(List<AudioFile> audioFiles, 
        Dictionary<int, string>? micAssignments = null,
        Action<string, int, int>? progressCallback = null)
    {
        var results = new List<TranscriptionResult>();
        var totalFiles = audioFiles.Count;

        for (int i = 0; i < audioFiles.Count; i++)
        {
            var audioFile = audioFiles[i];
            progressCallback?.Invoke($"Processing {audioFile.FileName}", i + 1, totalFiles);

            try
            {
                // Upload file (with automatic MP3 conversion if needed)
                var audioUrl = await UploadFileAsync(audioFile.FilePath, audioFile);
                
                // Start transcription
                var transcriptionId = await StartTranscriptionAsync(audioUrl);
                
                // Wait for completion
                var result = await WaitForTranscriptionAsync(transcriptionId);
                result.source_file_path = audioFile.FilePath;
                
                // Update audio file with transcription completion
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.TranscriptionComplete;
                audioFile.TranscriptId = result.id;
                
                // Apply speaker name mapping if available
                var rawTranscript = result.result?.transcription?.full_transcript;
                audioFile.TranscriptText = micAssignments != null && !string.IsNullOrEmpty(rawTranscript) 
                    ? MapSpeakerLabelsToNames(rawTranscript, micAssignments, audioFile.FileName)
                    : rawTranscript;
                    
                audioFile.ProcessedAt = DateTime.UtcNow;
                
                results.Add(result);
                
                _logger.LogInformation("Successfully processed file {FilePath}", audioFile.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file {FilePath}", audioFile.FilePath);
                
                // Update audio file with failure status
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.Failed;
                audioFile.ConversionError = ex.Message;
                
                // Add a failed result to maintain order
                results.Add(new TranscriptionResult
                {
                    status = "error",
                    source_file_path = audioFile.FilePath,
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
    public bool sentences { get; set; } = true;  // Essential for precise timing
    public bool subtitles { get; set; } = false;
    public bool moderation { get; set; } = false;
    public bool diarization { get; set; } = true;
    public bool translation { get; set; } = false;
    public bool audio_to_llm { get; set; } = false;
    public bool display_mode { get; set; } = false;
    public bool summarization { get; set; } = true;
    public bool audio_enhancer { get; set; } = true;
    public bool chapterization { get; set; } = true;
    public bool custom_spelling { get; set; } = false;
    public bool detect_language { get; set; } = false;  // Disabled since always English
    public string language { get; set; } = "en";  // Explicitly set to English
    public bool name_consistency { get; set; } = true;  // Keep speaker names consistent
    public DiarizationConfig? diarization_config { get; set; }
    public bool sentiment_analysis { get; set; } = true;
    public bool diarization_enhanced { get; set; } = true;  // Better speaker detection
    public bool punctuation_enhanced { get; set; } = true;  // Better readability
    public bool enable_code_switching { get; set; } = false;
    public bool named_entity_recognition { get; set; } = true;
    public bool speaker_reidentification { get; set; } = true;  // Track speakers across breaks
    public bool accurate_words_timestamps { get; set; } = true;  // Critical for clip matching
    public bool skip_channel_deduplication { get; set; } = false;
    public bool structured_data_extraction { get; set; } = false;
}

public class DiarizationConfig
{
    public bool enhanced { get; set; } = true;  // Better speaker separation
    public int number_of_speakers { get; set; } = 6;
    public int min_speakers { get; set; } = 2;  // Realistic minimum for friend conversations
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