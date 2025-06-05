using MovieReviewApp.Models;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        // Set the base URL for the HttpClient
        _httpClient.BaseAddress = new Uri(_baseUrl);

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

                // Update audio file with MP3 details
                audioFile.ProcessingStatus = targetStatus;
                audioFile.ConvertedAt = DateTime.UtcNow;

                // Delete the original WAV file and update FilePath to point to MP3
                var originalWavPath = audioFile.FilePath;
                var originalWavDir = Path.GetDirectoryName(originalWavPath);

                try
                {
                    if (File.Exists(originalWavPath))
                    {
                        File.Delete(originalWavPath);
                        _logger.LogInformation("Deleted original WAV file: {FilePath}", originalWavPath);
                    }

                    // Clean up the source directory if it's now empty
                    if (!string.IsNullOrEmpty(originalWavDir) && Directory.Exists(originalWavDir))
                    {
                        var remainingFiles = Directory.GetFiles(originalWavDir, "*.*", SearchOption.AllDirectories);
                        if (remainingFiles.Length == 0)
                        {
                            Directory.Delete(originalWavDir, true);
                            _logger.LogInformation("Cleaned up empty source folder: {FolderPath}", originalWavDir);
                        }
                    }
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Failed to delete original WAV file or cleanup folder: {FilePath}", originalWavPath);
                }

                // Update FilePath to point to the new MP3 file
                audioFile.FilePath = mp3Path;

                results.Add((audioFile, true, null));
                _logger.LogInformation("Converted {FileName} to MP3: {Mp3Path}",
                    Path.GetFileName(audioFile.FilePath), mp3Path);
            }
            catch (Exception ex)
            {
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.Failed;
                audioFile.ConversionError = ex.Message;

                // Move failed file to failed folder with cleanup
                audioFile.FilePath = await _audioOrganizer.MoveFileToStatusFolder(audioFile, sessionFolderPath, cleanupSource: true);

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
        Action<string, int, int>? progressCallback = null,
        MovieSession? session = null,
        Func<MovieSession, Task>? saveSessionCallback = null)
    {
        var results = new List<(AudioFile audioFile, bool success, string? error)>();
        // Process all MP3 files that can be reprocessed (pending, processed, or failed) and haven't been uploaded to Gladia yet
        var mp3Files = audioFiles.Where(f => (f.ProcessingStatus == AudioProcessingStatus.PendingMp3 ||
                                             f.ProcessingStatus == AudioProcessingStatus.ProcessedMp3 ||
                                             f.ProcessingStatus == AudioProcessingStatus.FailedMp3) &&
                                           string.IsNullOrEmpty(f.AudioUrl) &&
                                           f.FilePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)).ToList();

        // Log skipped files that are already uploaded
        var alreadyUploaded = audioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.UploadedToGladia ||
                                                  !string.IsNullOrEmpty(f.AudioUrl)).ToList();
        if (alreadyUploaded.Any())
        {
            _logger.LogInformation("Skipping {SkippedCount} files already uploaded to Gladia: {FileNames}",
                alreadyUploaded.Count, string.Join(", ", alreadyUploaded.Select(f => Path.GetFileName(f.FilePath))));
        }
        var totalFiles = mp3Files.Count;

        _logger.LogInformation("Starting Gladia upload for {TotalFiles} MP3 files", totalFiles);

        for (int i = 0; i < mp3Files.Count; i++)
        {
            var audioFile = mp3Files[i];
            progressCallback?.Invoke($"Uploading {Path.GetFileName(audioFile.FilePath)}", i + 1, totalFiles);

            try
            {
                var audioUrl = await UploadSingleFileToGladiaAsync(audioFile.FilePath);

                // Store the audio URL for the transcription phase
                audioFile.AudioUrl = audioUrl;
                audioFile.UploadedAt = DateTime.UtcNow;
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.UploadedToGladia;

                // Save session state immediately to persist upload progress
                if (session != null && saveSessionCallback != null)
                {
                    try
                    {
                        await saveSessionCallback(session);
                        _logger.LogInformation("Session state saved after uploading {FileName}", Path.GetFileName(audioFile.FilePath));
                    }
                    catch (Exception saveEx)
                    {
                        _logger.LogWarning(saveEx, "Failed to save session state after uploading {FileName}", Path.GetFileName(audioFile.FilePath));
                    }
                }

                results.Add((audioFile, true, null));
                _logger.LogInformation("Uploaded {FileName} to Gladia: {AudioUrl}",
                    Path.GetFileName(audioFile.FilePath), audioUrl);
            }
            catch (Exception ex)
            {
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.FailedMp3;
                audioFile.ConversionError = ex.Message;

                // Move MP3 file to failed_mp3 folder with cleanup
                audioFile.FilePath = await _audioOrganizer.MoveFileToStatusFolder(audioFile, sessionFolderPath, cleanupSource: true);

                results.Add((audioFile, false, ex.Message));
                _logger.LogError(ex, "Failed to upload {FileName} to Gladia", Path.GetFileName(audioFile.FilePath));
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
    /// Upload a single file to Gladia with retry logic and return the transcript ID
    /// </summary>
    private async Task<string> UploadSingleFileToGladiaAsync(string filePath)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Upload attempt {Attempt}/{MaxRetries} for {FileName}",
                    attempt, maxRetries, Path.GetFileName(filePath));

                return await UploadSingleFileToGladiaAsyncInternal(filePath);
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
            {
                var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                _logger.LogWarning("Upload attempt {Attempt} failed for {FileName}, retrying in {Delay}ms: {Error}",
                    attempt, Path.GetFileName(filePath), delay, ex.Message);

                await Task.Delay(delay);
            }
        }

        // Final attempt without retry wrapper
        return await UploadSingleFileToGladiaAsyncInternal(filePath);
    }

    /// <summary>
    /// Check if an exception is retryable (network/timeout issues)
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TaskCanceledException ||
               ex is SocketException ||
               (ex is IOException && ex.Message.Contains("transport connection"));
    }

    /// <summary>
    /// Internal upload method without retry logic
    /// </summary>
    private async Task<string> UploadSingleFileToGladiaAsyncInternal(string filePath)
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

        // Use streaming instead of loading entire file into memory
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);
        using var fileContent = new StreamContent(fileStream);

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
        // Create custom filename with movie name and mic identifier
        var customFilename = CreateCustomFilename(filePath);
        form.Add(fileContent, "audio", customFilename);

        // Note: Transcription configuration is set during the transcription request, not upload

        var response = await _httpClient.PostAsync("/v2/upload", form);

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
            // Create custom filename with movie name and mic identifier
            var customFilename = CreateCustomFilename(filePath);
            form.Add(fileContent, "audio", customFilename);

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

    public async Task<string> StartTranscriptionAsync(string audioUrl, int numOfSpeakers, bool enableSpeakerDiarization = true, string? fileName = null)
    {
        try
        {
            // Determine actual speaker count based on file type
            int actualSpeakerCount = Math.Max(1, numOfSpeakers); // Ensure at least 1 speaker
            bool isIndividualMic = false;
            
            if (!string.IsNullOrEmpty(fileName))
            {
                var upperFileName = fileName.ToUpperInvariant();
                
                // Individual mic files have only 1 speaker
                if (System.Text.RegularExpressions.Regex.IsMatch(upperFileName, @"^MIC\d+\.(WAV|MP3)$") ||
                    upperFileName == "PHONE.WAV" || upperFileName == "PHONE.MP3" ||
                    upperFileName == "SOUND_PAD.WAV" || upperFileName == "SOUNDPAD.WAV" ||
                    upperFileName == "SOUND_PAD.MP3" || upperFileName == "SOUNDPAD.MP3" ||
                    upperFileName == "USB.WAV" || upperFileName == "USB.MP3")
                {
                    actualSpeakerCount = 1;
                    isIndividualMic = true;
                    _logger.LogInformation("Detected individual mic file {FileName}, using 1 speaker with enhanced diarization", fileName);
                }
                // Mix files have multiple speakers
                else if (upperFileName.Contains("MIX") || upperFileName.Contains("MASTER"))
                {
                    // Use the provided numOfSpeakers for mix files, but ensure at least 2 for mix
                    actualSpeakerCount = Math.Max(2, numOfSpeakers);
                    _logger.LogInformation("Detected mix file {FileName}, using {SpeakerCount} speakers", fileName, actualSpeakerCount);
                }
            }
            
            // Comprehensive request to get detailed transcription data including timestamps, emphasis, confidence, etc.
            var request = new TranscriptionRequest
            {
                audio_url = audioUrl,
                diarization = enableSpeakerDiarization && !isIndividualMic,  // Disable diarization for individual mics
                language = "en",
                sentences = true,  // Essential for precise timing
                subtitles = false,
                moderation = false,
                translation = false,
                audio_to_llm = false,
                display_mode = false,
                summarization = true,  // Get AI summary and key points
                audio_enhancer = true,  // Better audio quality for transcription
                chapterization = true,  // Automatically divide into chapters
                custom_spelling = false,
                detect_language = false,  // Disabled since always English
                name_consistency = true,  // Keep speaker names consistent
                sentiment_analysis = true,  // Detect emotional emphasis
                diarization_enhanced = actualSpeakerCount <= 2,  // Enhanced only for 1-2 speakers
                punctuation_enhanced = true,  // Better readability
                enable_code_switching = false,
                named_entity_recognition = true,  // Identify names, places, etc.
                speaker_reidentification = true,  // Track speakers across breaks
                accurate_words_timestamps = true,  // Critical for matching audio clips
                skip_channel_deduplication = false,
                structured_data_extraction = false,
                diarization_config = enableSpeakerDiarization && !isIndividualMic ? new DiarizationConfig
                {
                    enhanced = actualSpeakerCount <= 2,  // Enhanced only works for 1-2 speakers
                    number_of_speakers = actualSpeakerCount,
                    min_speakers = Math.Max(1, actualSpeakerCount - 1),
                    max_speakers = Math.Min(8, actualSpeakerCount + 1)
                } : null
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/v2/pre-recorded", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Transcription request failed with {StatusCode}: {ErrorContent}. Request JSON: {RequestJson}",
                    response.StatusCode, errorContent, json);
                throw new HttpRequestException($"Transcription failed: {response.StatusCode} - {errorContent}");
            }

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

    /// <summary>
    /// Saves the complete JSON transcription result to disk alongside the audio file
    /// </summary>
    public async Task<string> SaveTranscriptionJsonAsync(TranscriptionResult transcriptionResult, string audioFilePath, string sessionFolderPath)
    {
        try
        {
            if (string.IsNullOrEmpty(audioFilePath))
            {
                throw new ArgumentException("Audio file path cannot be null or empty", nameof(audioFilePath));
            }

            // Get the directory where the audio file is located
            var audioDirectory = Path.GetDirectoryName(audioFilePath) ?? sessionFolderPath;

            // Create JSON filename based on audio filename
            var audioFileName = Path.GetFileNameWithoutExtension(audioFilePath);
            var jsonFileName = $"{audioFileName}_transcription.json";
            var jsonFilePath = Path.Combine(audioDirectory, jsonFileName);

            // Serialize the complete transcription result with pretty formatting
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonContent = JsonSerializer.Serialize(transcriptionResult, jsonOptions);

            // Write JSON file
            await File.WriteAllTextAsync(jsonFilePath, jsonContent);

            _logger.LogInformation("Saved complete transcription JSON for {AudioFile} to {JsonPath}",
                Path.GetFileName(audioFilePath), jsonFilePath);

            return jsonFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save transcription JSON for {AudioFile}", audioFilePath);
            throw;
        }
    }

    /// <summary>
    /// Enhanced transcription method that saves both the transcript text and complete JSON
    /// </summary>
    public async Task<(TranscriptionResult result, string jsonPath)> ProcessTranscriptionWithJsonSaveAsync(
        string audioUrl,
        string audioFilePath,
        string sessionFolderPath,
        int numOfSpeakers,
        bool enableSpeakerDiarization = true)
    {
        try
        {
            // Start transcription with filename for proper speaker detection
            var fileName = Path.GetFileName(audioFilePath);
            var transcriptionId = await StartTranscriptionAsync(audioUrl, numOfSpeakers, enableSpeakerDiarization, fileName);
            _logger.LogInformation("Started transcription {TranscriptionId} for {AudioFile}",
                transcriptionId, fileName);

            // Wait for completion
            var result = await WaitForTranscriptionAsync(transcriptionId);
            result.source_file_path = audioFilePath;

            // Save complete JSON alongside audio file
            var jsonPath = await SaveTranscriptionJsonAsync(result, audioFilePath, sessionFolderPath);

            _logger.LogInformation("Completed transcription and saved JSON for {AudioFile}",
                Path.GetFileName(audioFilePath));

            return (result, jsonPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process transcription with JSON save for {AudioFile}", audioFilePath);
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

    /// <summary>
    /// Creates a custom filename for Gladia upload using movie name from folder path
    /// </summary>
    private string CreateCustomFilename(string filePath)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(fileName);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            
            // Get the folder path to extract movie name
            var directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                return fileName; // Fallback to original filename
            }
            
            // Navigate up to find the session folder (YYYY-MonthName-MovieTitle)
            var currentDir = new DirectoryInfo(directoryPath);
            while (currentDir != null)
            {
                var folderName = currentDir.Name;
                
                // Check if this matches the YYYY-MonthName-MovieTitle pattern
                var monthNameMatch = Regex.Match(folderName, @"(\d{4})-([A-Za-z]+)-(.+)");
                if (monthNameMatch.Success)
                {
                    var moviePart = monthNameMatch.Groups[3].Value;
                    // Convert hyphens to spaces for movie title
                    var movieTitle = moviePart.Replace("-", " ").Trim();
                    
                    // Create custom filename: MovieTitle_OriginalFilename.ext
                    var customName = $"{movieTitle}_{fileNameWithoutExt}{extension}";
                    _logger.LogInformation("Created custom filename for Gladia: {CustomName} (from {OriginalName})", 
                        customName, fileName);
                    return customName;
                }
                
                currentDir = currentDir.Parent;
            }
            
            // Fallback to original filename if no matching pattern found
            _logger.LogDebug("No movie folder pattern found for {FilePath}, using original filename", filePath);
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create custom filename for {FilePath}, using original", filePath);
            return Path.GetFileName(filePath);
        }
    }

    public async Task<List<TranscriptionResult>> ProcessMultipleFilesAsync(List<AudioFile> audioFiles,
        Dictionary<int, string>? micAssignments = null,
        Action<string, int, int>? progressCallback = null)
    {
        var results = new List<TranscriptionResult>();
        var totalFiles = audioFiles.Count;
        
        // Calculate number of speakers, ensuring at least 1
        int numOfSpeakers = 2; // Default for when no assignments
        if (micAssignments != null && micAssignments.Any())
        {
            var assignedCount = micAssignments.Values.Count(v => !string.IsNullOrWhiteSpace(v));
            if (assignedCount > 0)
            {
                numOfSpeakers = assignedCount;
            }
        }
        _logger.LogInformation("Processing {FileCount} files with {SpeakerCount} speakers", totalFiles, numOfSpeakers);
        for (int i = 0; i < audioFiles.Count; i++)
        {
            var audioFile = audioFiles[i];
            progressCallback?.Invoke($"Processing {audioFile.FileName}", i + 1, totalFiles);

            try
            {
                // Upload file (with automatic MP3 conversion if needed)
                var audioUrl = await UploadFileAsync(audioFile.FilePath, audioFile);

                // Process transcription and save complete JSON
                var sessionFolderPath = Path.GetDirectoryName(Path.GetDirectoryName(audioFile.FilePath)) ??
                                       Path.GetDirectoryName(audioFile.FilePath) ??
                                       Directory.GetCurrentDirectory();

                var (result, jsonPath) = await ProcessTranscriptionWithJsonSaveAsync(
                    audioUrl, audioFile.FilePath, sessionFolderPath, numOfSpeakers);

                // Update audio file with transcription completion
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.TranscriptionComplete;
                audioFile.TranscriptId = result.id;
                audioFile.JsonFilePath = jsonPath; // Store path to JSON file

                // Apply speaker name mapping if available
                var rawTranscript = result.result?.transcription?.full_transcript;
                audioFile.TranscriptText = micAssignments != null && !string.IsNullOrEmpty(rawTranscript)
                    ? MapSpeakerLabelsToNames(rawTranscript, micAssignments, audioFile.FileName)
                    : rawTranscript;

                audioFile.ProcessedAt = DateTime.UtcNow;

                results.Add(result);

                _logger.LogInformation("Successfully processed file {FilePath} and saved JSON to {JsonPath}",
                    audioFile.FilePath, jsonPath);
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
    public SentimentAnalysis? sentiment_analysis { get; set; }
    public List<NamedEntity>? named_entities { get; set; }
    public AudioMetadata? audio_metadata { get; set; }
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
    public bool? emphasis { get; set; }
    public string? sentiment { get; set; }
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

public class SentimentAnalysis
{
    public string? overall_sentiment { get; set; }
    public double overall_confidence { get; set; }
    public List<SentimentSegment>? segments { get; set; }
}

public class SentimentSegment
{
    public double start { get; set; }
    public double end { get; set; }
    public string sentiment { get; set; } = string.Empty;
    public double confidence { get; set; }
    public int? speaker { get; set; }
}

public class NamedEntity
{
    public string text { get; set; } = string.Empty;
    public string category { get; set; } = string.Empty;
    public double start { get; set; }
    public double end { get; set; }
    public double confidence { get; set; }
    public int? speaker { get; set; }
}

public class AudioMetadata
{
    public string? filename { get; set; }
    public string? extension { get; set; }
    public long? size { get; set; }
    public double? audio_duration { get; set; }
    public int? number_of_channels { get; set; }
    public int? sample_rate { get; set; }
    public string? format { get; set; }
}