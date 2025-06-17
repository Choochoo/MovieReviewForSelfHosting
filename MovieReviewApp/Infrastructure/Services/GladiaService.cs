using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Infrastructure.Constants;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Models;

namespace MovieReviewApp.Infrastructure.Services;

public class GladiaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GladiaService> _logger;
    private readonly SecretsManager _secretsManager;
    private readonly AudioFileOrganizer _audioOrganizer;
    private readonly string _apiKey;
    private readonly string _baseUrl = "https://api.gladia.io";

    public GladiaService(
        HttpClient httpClient,
        IConfiguration configuration,
        SecretsManager secretsManager,
        AudioFileOrganizer audioOrganizer,
        ILogger<GladiaService> logger)
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

    /// <summary>
    /// Gets a value indicating whether the Gladia service is properly configured with an API key.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    /// <summary>
    /// Logs the current configuration status of the Gladia service for debugging purposes.
    /// </summary>
    public void LogConfigurationStatus()
    {
        IEnumerable<KeyValuePair<string, string?>> allConfigKeys = _configuration.AsEnumerable().Where(kvp => kvp.Key.Contains("Gladia", StringComparison.OrdinalIgnoreCase));
        string? secretValue = _secretsManager.GetSecret("Gladia:ApiKey");

        _logger.LogInformation("Gladia configuration status:");
        _logger.LogInformation("  IsConfigured: {IsConfigured}", IsConfigured);
        _logger.LogInformation("  API Key present: {HasApiKey}", !string.IsNullOrEmpty(_apiKey));
        _logger.LogInformation("  API Key length: {KeyLength}", _apiKey?.Length ?? 0);
        _logger.LogInformation("  SecretsManager value length: {SecretLength}", secretValue?.Length ?? 0);
        _logger.LogInformation("  Configuration source:");

        foreach (KeyValuePair<string, string?> configItem in allConfigKeys)
        {
            string maskedValue = string.IsNullOrEmpty(configItem.Value) ? "[NULL]" :
                             configItem.Value.Length > 10 ? $"{configItem.Value[..10]}..." : "[SHORT]";
            _logger.LogInformation("    Config[{Key}]: {Value}", configItem.Key, maskedValue);
        }
    }

    private bool IsFFmpegAvailable()
    {
        try
        {
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using System.Diagnostics.Process process = new System.Diagnostics.Process();
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

    public async Task<string> ConvertToMp3Async(string inputPath, string outputPath, Action<string, int, int>? progressCallback = null)
    {
        try
        {
            _logger.LogInformation("Converting {InputFile} to MP3", Path.GetFileName(inputPath));

            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{inputPath}\" -codec:a libmp3lame -b:a 192k -ar 44100 -ac 2 -af \"volume=1.5\" \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            using System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo = startInfo;
            process.Start();

            // Fake progress loader with reliable process monitoring
            int progress = 0;
            TimeSpan maxWaitTime = TimeSpan.FromMinutes(10);
            DateTime startTime = DateTime.UtcNow;

            // Progress loop using short WaitForExit calls
            while ((DateTime.UtcNow - startTime) < maxWaitTime)
            {
                // Check if process finished with a short timeout
                if (process.WaitForExit(2200))
                {
                    // Process finished
                    break;
                }

                // Update progress
                progress = Math.Min(97, progress + 3); // Cap at 97% until process finishes
                string progressMessage = $"Converting {Path.GetFileName(inputPath)} ({progress}%)";
                progressCallback?.Invoke(progressMessage, 1, 1);
                _logger.LogDebug("MP3 Conversion - Converting {FileName}: {Progress}%",
                    Path.GetFileName(inputPath), progress);
            }

            // Check if process timed out
            if (!process.HasExited)
            {
                process.Kill();
                throw new TimeoutException($"FFmpeg conversion timed out after 10 minutes for {Path.GetFileName(inputPath)}");
            }

            // Set progress to 100% when done
            string finalProgressMessage = $"Converting {Path.GetFileName(inputPath)} (100%)";
            progressCallback?.Invoke(finalProgressMessage, 1, 1);
            _logger.LogDebug("MP3 Conversion - Converting {FileName}: {Progress}%",
                Path.GetFileName(inputPath), 100);

            if (process.ExitCode != 0)
            {
                throw new Exception($"FFmpeg conversion failed with exit code {process.ExitCode} for {Path.GetFileName(inputPath)}");
            }

            long originalSize = new FileInfo(inputPath).Length;
            long compressedSize = new FileInfo(outputPath).Length;
            double compressionRatio = (double)compressedSize / originalSize;

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

    // Helper to check if a file is locked
    private bool IsFileLocked(string filePath)
    {
        try
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Close();
            }
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>
    /// Maps Gladia speaker labels to actual participant names based on file type
    /// For master mix files, uses timestamp-based mapping from individual mic files
    /// </summary>
    public string BuildTranscriptFromUtterances(TranscriptionResult result, Dictionary<int, string> micAssignments, string fileName)
    {
        return BuildTranscriptFromUtterances(result, micAssignments, fileName, null);
    }

    /// <summary>
    /// Maps Gladia speaker labels to actual participant names based on file type
    /// For master mix files, uses timestamp-based mapping from individual mic transcript data
    /// </summary>
    public string BuildTranscriptFromUtterances(TranscriptionResult result, Dictionary<int, string> micAssignments, string fileName, List<(TranscriptionResult micResult, string participantName)>? individualMicData)
    {
        if (result?.result?.transcription?.utterances == null || !result.result.transcription.utterances.Any())
        {
            _logger.LogWarning("BuildTranscriptFromUtterances called with no utterances for file {FileName}", fileName);
            return result?.result?.transcription?.full_transcript ?? string.Empty;
        }

        if (!micAssignments.Any())
        {
            _logger.LogWarning("BuildTranscriptFromUtterances called with no mic assignments for file {FileName}", fileName);
            return result?.result?.transcription?.full_transcript ?? string.Empty;
        }

        _logger.LogInformation("Building transcript from {UtteranceCount} utterances for file {FileName} with mic assignments: {Assignments}",
            result.result.transcription.utterances.Count, fileName,
            string.Join(", ", micAssignments.Select(kvp => $"Mic{kvp.Key}={kvp.Value}")));

        List<string> transcriptLines = new List<string>();
        string upperFileName = fileName.ToUpper();

        // Check if this is an individual mic file (MIC1.WAV, MIC2.WAV, etc.)
        Match micMatch = System.Text.RegularExpressions.Regex.Match(upperFileName, @"^MIC(\d+)\.(?:WAV|MP3)$");
        if (micMatch.Success)
        {
            int fileBasedMicNumber = int.Parse(micMatch.Groups[1].Value); // 1-based from filename (MIC1.mp3 = 1)
            int micNumber = fileBasedMicNumber - 1; // Convert to 0-based for mic assignments lookup
            _logger.LogInformation("DETECTED INDIVIDUAL MIC FILE: {FileName} -> file number {FileNumber} -> internal mic number {MicNumber}", fileName, fileBasedMicNumber, micNumber);
            _logger.LogInformation("AVAILABLE MIC ASSIGNMENTS: {Assignments}",
                string.Join(", ", micAssignments.Select(kvp => $"[{kvp.Key}]='{kvp.Value}'")));
            _logger.LogInformation("LOOKING FOR MIC ASSIGNMENT: micAssignments[{MicNumber}] (converted from file mic {FileNumber})", micNumber, fileBasedMicNumber);

            // For individual mic files, all utterances belong to the mic owner
            // The speaker ID in utterances is always 0 for individual files since it's just one person
            if (micAssignments.TryGetValue(micNumber, out string? participantName) && !string.IsNullOrEmpty(participantName))
            {
                _logger.LogInformation("BUILDING TRANSCRIPT: Processing {UtteranceCount} utterances for {ParticipantName} from mic {MicNumber}",
                    result.result.transcription.utterances.Count, participantName, micNumber);

                foreach (dynamic utterance in result.result.transcription.utterances)
                {
                    if (!string.IsNullOrWhiteSpace(utterance.text))
                    {
                        string transcriptLine = $"{participantName}: {utterance.text.Trim()}";
                        transcriptLines.Add(transcriptLine);
                        _logger.LogTrace("ADDED TRANSCRIPT LINE: {Line}", transcriptLine.Substring(0, Math.Min(100, transcriptLine.Length)));
                    }
                }
                _logger.LogInformation("Built transcript for individual mic {FileName} (internal mic {MicNumber}, file mic {FileNumber}) with {LineCount} lines for {ParticipantName}",
                    fileName, micNumber, fileBasedMicNumber, transcriptLines.Count, participantName);
            }
            else
            {
                _logger.LogWarning("No participant name found for mic {MicNumber} (file mic {FileNumber}) in file {FileName}. Available assignments: {Assignments}",
                    micNumber, fileBasedMicNumber, fileName, string.Join(", ", micAssignments.Select(kvp => $"[{kvp.Key}]='{kvp.Value}'")));

                // Fallback: use generic name based on file number for display
                foreach (dynamic utterance in result.result.transcription.utterances)
                {
                    if (!string.IsNullOrWhiteSpace(utterance.text))
                    {
                        transcriptLines.Add($"Mic {fileBasedMicNumber}: {utterance.text.Trim()}");
                    }
                }
            }
        }
        else
        {
            // For master/mix files, use timestamp-based mapping if individual mic data is available
            _logger.LogInformation("Processing master/mix file {FileName}", fileName);

            if (individualMicData != null && individualMicData.Any())
            {
                _logger.LogInformation("Using timestamp-based speaker mapping for master mix with {MicCount} individual mic files", individualMicData.Count);
                transcriptLines = BuildTimestampBasedTranscript(result, individualMicData);
            }
            else
            {
                // Fallback: Use Gladia's speaker diarization (less reliable)
                _logger.LogWarning("No individual mic data available for {FileName}, falling back to Gladia speaker diarization", fileName);

                foreach (dynamic utterance in result.result.transcription.utterances)
                {
                    if (!string.IsNullOrWhiteSpace(utterance.text))
                    {
                        // Map Gladia speaker number (0-based) to participant name using mic assignments
                        // Try to map speaker 0 -> mic 0, speaker 1 -> mic 1, etc. (corrected to 0-based)
                        int micNumber = utterance.speaker;

                        if (micAssignments.TryGetValue(micNumber, out string? participantName) && !string.IsNullOrEmpty(participantName))
                        {
                            transcriptLines.Add($"{participantName}: {utterance.text.Trim()}");
                            _logger.LogTrace("Mapped speaker {SpeakerNum} to {ParticipantName} in master mix", (int)utterance.speaker, participantName);
                        }
                        else
                        {
                            // Fallback to generic speaker label if no mapping found
                            transcriptLines.Add($"Speaker {utterance.speaker + 1}: {utterance.text.Trim()}");
                            _logger.LogTrace("No mapping found for speaker {SpeakerNum}, using generic label", (int)utterance.speaker);
                        }
                    }
                }
            }

            int uniqueSpeakers = result.result.transcription.utterances.Select(u => u.speaker).Distinct().Count();
            _logger.LogInformation("Built transcript for master/mix file {FileName} with {LineCount} lines from {SpeakerCount} detected speakers",
                fileName, transcriptLines.Count, uniqueSpeakers);
        }

        string finalTranscript = string.Join("\n", transcriptLines);

        _logger.LogInformation("FINAL TRANSCRIPT for {FileName}:", fileName);
        _logger.LogInformation("  - Generated {LineCount} transcript lines", transcriptLines.Count);
        _logger.LogInformation("  - Total transcript length: {Length} characters", finalTranscript.Length);
        _logger.LogInformation("  - Lines contain newlines: {HasNewlines}", finalTranscript.Contains('\n'));
        _logger.LogInformation("  - Number of newlines: {NewlineCount}", finalTranscript.Count(c => c == '\n'));
        _logger.LogInformation("  - First 500 characters: {Preview}",
            finalTranscript.Length > 0 ? finalTranscript.Substring(0, Math.Min(500, finalTranscript.Length)) : "[EMPTY]");

        if (transcriptLines.Count > 0)
        {
            _logger.LogInformation("  - Sample lines: {SampleLines}",
                string.Join(" | ", transcriptLines.Take(3).Select(l => l.Length > 50 ? l.Substring(0, 50) + "..." : l)));
        }

        // Log the exact transcript that will be analyzed
        _logger.LogInformation("TRANSCRIPT FOR ANALYSIS (first 10 lines):");
        string[] analysisLines = finalTranscript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < Math.Min(10, analysisLines.Length); i++)
        {
            _logger.LogInformation("  Line {LineNum}: {Line}", i + 1, analysisLines[i].Length > 100 ? analysisLines[i].Substring(0, 100) + "..." : analysisLines[i]);
        }

        return finalTranscript;
    }

    /// <summary>
    /// Builds a transcript for master mix files using timestamp-based mapping from individual mic files
    /// This provides much more accurate speaker attribution than Gladia's speaker diarization
    /// </summary>
    private List<string> BuildTimestampBasedTranscript(TranscriptionResult masterMixResult, List<(TranscriptionResult micResult, string participantName)> individualMicData)
    {
        List<string> transcriptLines = new List<string>();

        _logger.LogInformation("Building timestamp-based transcript from master mix with {UtteranceCount} utterances",
            masterMixResult.result.transcription.utterances.Count);

        foreach (dynamic masterUtterance in masterMixResult.result.transcription.utterances)
        {
            if (string.IsNullOrWhiteSpace(masterUtterance.text))
                continue;

            string? bestMatchSpeaker = null;
            double bestMatchScore = 0;
            string? bestMatchText = null;

            // Check each individual mic file to see which participant's timing best matches this utterance
            foreach ((TranscriptionResult micResult, string participantName) in individualMicData)
            {
                if (micResult?.result?.transcription?.utterances == null)
                    continue;

                // Find the best matching utterance from this mic based on timestamp overlap
                foreach (dynamic micUtterance in micResult.result.transcription.utterances)
                {
                    if (string.IsNullOrWhiteSpace(micUtterance.text))
                        continue;

                    // Calculate overlap between master utterance and mic utterance
                    double overlapStart = Math.Max(masterUtterance.start, micUtterance.start);
                    double overlapEnd = Math.Min(masterUtterance.end, micUtterance.end);
                    double overlap = Math.Max(0, overlapEnd - overlapStart);

                    double masterDuration = masterUtterance.end - masterUtterance.start;
                    double micDuration = micUtterance.end - micUtterance.start;

                    // Calculate overlap percentage relative to both utterances
                    double overlapScore = overlap / Math.Max(masterDuration, micDuration);

                    // Also consider text similarity (simple approach)
                    double textSimilarity = CalculateTextSimilarity(masterUtterance.text, micUtterance.text);
                    double combinedScore = (overlapScore * 0.7) + (textSimilarity * 0.3);

                    if (combinedScore > bestMatchScore && combinedScore > 0.3) // Minimum threshold
                    {
                        bestMatchScore = combinedScore;
                        bestMatchSpeaker = participantName;
                        bestMatchText = micUtterance.text;
                    }
                }
            }

            // Add the utterance with the best matching speaker
            if (bestMatchSpeaker != null)
            {
                transcriptLines.Add($"{bestMatchSpeaker}: {masterUtterance.text.Trim()}");
                _logger.LogTrace("Matched master utterance [{Start:F1}s-{End:F1}s] to {Speaker} (score: {Score:F2})",
                    (double)masterUtterance.start, (double)masterUtterance.end, bestMatchSpeaker, bestMatchScore);
            }
            else
            {
                // No good match found, use generic speaker label
                transcriptLines.Add($"Unknown Speaker: {masterUtterance.text.Trim()}");
                _logger.LogTrace("No speaker match found for master utterance [{Start:F1}s-{End:F1}s]: {Text}",
                    (double)masterUtterance.start, (double)masterUtterance.end, ((string)masterUtterance.text).Substring(0, Math.Min(50, ((string)masterUtterance.text).Length)));
            }
        }

        _logger.LogInformation("Built timestamp-based transcript with {LineCount} lines", transcriptLines.Count);
        return transcriptLines;
    }

    /// <summary>
    /// Simple text similarity calculation for matching utterances
    /// </summary>
    private double CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0;

        HashSet<string> words1 = text1.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        HashSet<string> words2 = text2.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (words1.Count == 0 || words2.Count == 0)
            return 0;

        int intersection = words1.Intersect(words2).Count();
        int union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    public string MapSpeakerLabelsToNames(string transcriptText, Dictionary<int, string> micAssignments, string fileName)
    {
        if (string.IsNullOrEmpty(transcriptText))
        {
            _logger.LogWarning("MapSpeakerLabelsToNames called with empty transcript text");
            return transcriptText;
        }

        if (!micAssignments.Any())
        {
            _logger.LogWarning("MapSpeakerLabelsToNames called with no mic assignments");
            return transcriptText;
        }

        _logger.LogInformation("Mapping speaker labels for file {FileName} with {AssignmentCount} mic assignments: {Assignments}",
            fileName, micAssignments.Count,
            string.Join(", ", micAssignments.Select(kvp => $"Mic{kvp.Key}={kvp.Value}")));

        string result = transcriptText;
        string upperFileName = fileName.ToUpper();

        // Check if this is an individual mic file (MIC1.WAV, MIC2.WAV, etc.)
        Match micMatch = System.Text.RegularExpressions.Regex.Match(upperFileName, @"^MIC(\d)\.WAV$");
        if (micMatch.Success)
        {
            int micNumber = int.Parse(micMatch.Groups[1].Value);
            if (micAssignments.TryGetValue(micNumber, out string? participantName) && !string.IsNullOrEmpty(participantName))
            {
                // For individual mic files, replace ALL speaker labels with the mic owner's name
                int originalLength = result.Length;
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\bSpeaker \d+:", $"{participantName}:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\bspeaker \d+:", $"{participantName}:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\[Speaker \d+\]:", $"[{participantName}]:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\(Speaker \d+\):", $"({participantName}):");
                // Also handle variations without colons
                result = System.Text.RegularExpressions.Regex.Replace(result, @"^Speaker \d+\s", $"{participantName}: ", System.Text.RegularExpressions.RegexOptions.Multiline);
                result = System.Text.RegularExpressions.Regex.Replace(result, @"^speaker \d+\s", $"{participantName}: ", System.Text.RegularExpressions.RegexOptions.Multiline);

                int replacedChars = originalLength - result.Length;
                _logger.LogInformation("Mapped all speakers in {FileName} to {ParticipantName}, replaced {CharCount} characters",
                    fileName, participantName, Math.Abs(replacedChars));
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
            int replacementCount = 0;
            // Map Speaker 1, Speaker 2, etc. to actual names based on mic assignments
            for (int i = 1; i <= 10; i++) // Support up to 10 speakers
            {
                if (micAssignments.TryGetValue(i, out string? participantName) && !string.IsNullOrEmpty(participantName))
                {
                    (string oldPattern, string newPattern)[] patterns = new[]
                    {
                        ($"Speaker {i}:", $"{participantName}:"),
                        ($"Speaker {i} :", $"{participantName}:"),
                        ($"speaker {i}:", $"{participantName}:"),
                        ($"speaker {i} :", $"{participantName}:"),
                        ($"[Speaker {i}]:", $"[{participantName}]:"),
                        ($"(Speaker {i}):", $"({participantName}):")
                    };

                    foreach ((string oldPattern, string newPattern) in patterns)
                    {
                        int countBefore = result.Length;
                        result = result.Replace(oldPattern, newPattern);
                        if (result.Length != countBefore)
                        {
                            int occurrences = (countBefore - result.Length) / (oldPattern.Length - newPattern.Length);
                            replacementCount += occurrences;
                            _logger.LogDebug("Replaced {Count} occurrences of '{Old}' with '{New}'",
                                occurrences, oldPattern, newPattern);
                        }
                    }
                }
            }
            _logger.LogInformation("Applied speaker number mapping for {FileName}, made {ReplacementCount} replacements",
                fileName, replacementCount);
        }

        return result;
    }

    /// <summary>
    /// Upload a single file to Gladia with retry logic and return the transcript ID
    /// </summary>
    public async Task<string> UploadSingleFileToGladiaAsync(string filePath)
    {
        _logger.LogDebug("UploadSingleFileToGladiaAsync called for: {FilePath}", filePath);

        // Log configuration status first
        LogConfigurationStatus();

        const int maxRetries = GladiaConstants.MaxRetries;
        const int baseDelayMs = GladiaConstants.BaseDelayMs;

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
                int delay = baseDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                _logger.LogWarning("Upload attempt {Attempt} failed for {FileName}, retrying in {Delay}ms: {Message}",
                    attempt, Path.GetFileName(filePath), delay, ex.Message);

                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                _logger.LogError("Upload attempt {Attempt} failed with non-retryable exception: {ExceptionType}: {Message}",
                    attempt, ex.GetType().Name, ex.Message);
                throw;
            }
        }

        // Final attempt without retry wrapper
        _logger.LogError("All upload attempts failed for {FileName}", Path.GetFileName(filePath));
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
        _logger.LogDebug("Starting upload for file: {FilePath}", filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found: {FilePath}", filePath);
            throw new FileNotFoundException($"Audio file not found: {filePath}");
        }

        FileInfo fileInfo = new FileInfo(filePath);
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        _logger.LogInformation("Uploading file {FileName} ({FileSize:N0} bytes) to Gladia",
            Path.GetFileName(filePath), fileInfo.Length);

        // Validate API configuration
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("Gladia API key not configured");
        }

        using MultipartFormDataContent form = new MultipartFormDataContent();

        // Use streaming instead of loading entire file into memory
        using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: GladiaConstants.FileStreamBufferSize);
        using StreamContent fileContent = new StreamContent(fileStream);

        // Set appropriate content type based on file extension
        string contentType = extension switch
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
        string customFilename = CreateCustomFilename(filePath);
        form.Add(fileContent, "audio", customFilename);


        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync("/v2/upload", form);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gladia upload failed with status {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);

                throw new Exception($"Gladia upload failed with status {response.StatusCode}: {errorContent}");
            }

            string content = await response.Content.ReadAsStringAsync();
            UploadResponse? uploadResult = JsonSerializer.Deserialize<UploadResponse>(content);

            if (uploadResult?.audio_url == null)
            {
                _logger.LogError("Missing audio_url in Gladia response: {Content}", content);
                throw new Exception($"Gladia upload response missing audio_url: {content}");
            }

            _logger.LogInformation("Successfully uploaded {FileName} to Gladia with URL: {AudioUrl}",
                Path.GetFileName(filePath), uploadResult.audio_url);

            return uploadResult.audio_url;
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP request exception during Gladia upload: {Message}", httpEx.Message);
            throw;
        }
        catch (TaskCanceledException tcEx)
        {
            _logger.LogError(tcEx, "Upload request timed out: {Message}", tcEx.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "General exception during Gladia upload: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
            throw;
        }
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

            FileInfo fileInfo = new FileInfo(filePath);
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            // Determine if we should convert to MP3 for better upload performance
            const long LARGE_FILE_THRESHOLD = 100 * 1024 * 1024; // 100MB
            bool shouldConvert = fileInfo.Length > LARGE_FILE_THRESHOLD && extension == ".wav";

            string actualFilePath = filePath;

            if (shouldConvert)
            {
                if (!IsFFmpegAvailable())
                {
                    string errorMessage = $"File {Path.GetFileName(filePath)} is too large ({fileInfo.Length:N0} bytes > {LARGE_FILE_THRESHOLD:N0}) and requires FFmpeg for MP3 conversion. Please install FFmpeg or use smaller files.";
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
                    audioFile.ProcessingStatus = Models.AudioProcessingStatus.FinishedConvertingToMp3;
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
                        audioFile.ProcessingStatus = Models.AudioProcessingStatus.FinishedConvertingToMp3;
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

            using MultipartFormDataContent form = new MultipartFormDataContent();

            // Create file stream with explicit buffer size for large files
            using FileStream fileStream = new FileStream(actualFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);

            // Create a byte array to ensure the stream is fully read before sending
            FileInfo actualFileInfo = new FileInfo(actualFilePath);
            byte[] fileBytes = new byte[actualFileInfo.Length];
            await fileStream.ReadAsync(fileBytes, 0, fileBytes.Length);

            using ByteArrayContent fileContent = new ByteArrayContent(fileBytes);

            // Set appropriate content type based on file extension
            string contentType = extension switch
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
            string customFilename = CreateCustomFilename(filePath);
            form.Add(fileContent, "audio", customFilename);

            HttpResponseMessage response = await _httpClient.PostAsync("/v2/upload", form);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gladia upload failed with status {StatusCode}: {ErrorContent}",
                    response.StatusCode, errorContent);
                response.EnsureSuccessStatusCode();
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            UploadResponse? uploadResult = JsonSerializer.Deserialize<UploadResponse>(jsonResponse);

            string audioUrl = uploadResult?.audio_url ?? throw new Exception("Failed to get upload URL from response");
            _logger.LogInformation("Successfully uploaded {FileName} to Gladia, got URL: {AudioUrl}",
                Path.GetFileName(actualFilePath), audioUrl);

            // Update audio file status if provided
            if (audioFile != null)
            {
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.FinishedUploadingToGladia;
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
                string upperFileName = fileName.ToUpperInvariant();

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
            TranscriptionRequest request = new TranscriptionRequest
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

            string json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync("/v2/pre-recorded", content);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Transcription request failed with {StatusCode}: {ErrorContent}. Request JSON: {RequestJson}",
                    response.StatusCode, errorContent, json);
                throw new HttpRequestException($"Transcription failed: {response.StatusCode} - {errorContent}");
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            TranscriptionResponse? transcriptionResult = JsonSerializer.Deserialize<TranscriptionResponse>(jsonResponse);

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
            HttpResponseMessage response = await _httpClient.GetAsync($"{_baseUrl}/v2/pre-recorded/{transcriptionId}");
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();
            TranscriptionResult? result = JsonSerializer.Deserialize<TranscriptionResult>(jsonResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

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
            string audioDirectory = Path.GetDirectoryName(audioFilePath) ?? sessionFolderPath;

            // Create JSON filename based on audio filename
            string audioFileName = Path.GetFileNameWithoutExtension(audioFilePath);
            string jsonFileName = $"{audioFileName}_transcription.json";
            string jsonFilePath = Path.Combine(audioDirectory, jsonFileName);

            // Serialize the complete transcription result with pretty formatting
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            string jsonContent = JsonSerializer.Serialize(transcriptionResult, jsonOptions);

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
            string fileName = Path.GetFileName(audioFilePath);
            string transcriptionId = await StartTranscriptionAsync(audioUrl, numOfSpeakers, enableSpeakerDiarization, fileName);
            _logger.LogInformation("Started transcription {TranscriptionId} for {AudioFile}",
                transcriptionId, fileName);

            // Wait for completion
            TranscriptionResult result = await WaitForTranscriptionAsync(transcriptionId);
            result.source_file_path = audioFilePath;

            // Save complete JSON alongside audio file
            string jsonPath = await SaveTranscriptionJsonAsync(result, audioFilePath, sessionFolderPath);

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
        TimeSpan maxWaitTime = TimeSpan.FromMinutes(maxWaitTimeMinutes);
        DateTime startTime = DateTime.UtcNow;
        TimeSpan checkInterval = TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            TranscriptionResult result = await GetTranscriptionResultAsync(transcriptionId);

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
            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(fileName);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

            // Get the folder path to extract movie name
            string? directoryPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                return fileName; // Fallback to original filename
            }

            // Navigate up to find the session folder (YYYY-MonthName-MovieTitle)
            DirectoryInfo currentDir = new DirectoryInfo(directoryPath);
            while (currentDir != null)
            {
                string folderName = currentDir.Name;

                // Check if this matches the YYYY-MonthName-MovieTitle pattern
                Match monthNameMatch = Regex.Match(folderName, @"(\d{4})-([A-Za-z]+)-(.+)");
                if (monthNameMatch.Success)
                {
                    string moviePart = monthNameMatch.Groups[3].Value;
                    // Convert hyphens to spaces for movie title
                    string movieTitle = moviePart.Replace("-", " ").Trim();

                    // Create custom filename: MovieTitle_OriginalFilename.ext
                    string customName = $"{movieTitle}_{fileNameWithoutExt}{extension}";
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
        List<TranscriptionResult> results = new List<TranscriptionResult>();
        int totalFiles = audioFiles.Count;

        // Calculate number of speakers, ensuring at least 1
        int numOfSpeakers = 2; // Default for when no assignments
        if (micAssignments != null && micAssignments.Any())
        {
            int assignedCount = micAssignments.Values.Count(v => !string.IsNullOrWhiteSpace(v));
            if (assignedCount > 0)
            {
                numOfSpeakers = assignedCount;
            }
        }
        _logger.LogInformation("Processing {FileCount} files with {SpeakerCount} speakers", totalFiles, numOfSpeakers);
        for (int i = 0; i < audioFiles.Count; i++)
        {
            AudioFile audioFile = audioFiles[i];
            progressCallback?.Invoke($"Processing {audioFile.FileName}", i + 1, totalFiles);

            try
            {
                // Upload file (with automatic MP3 conversion if needed)
                string audioUrl = await UploadFileAsync(audioFile.FilePath, audioFile);

                // Process transcription and save complete JSON
                string sessionFolderPath = Path.GetDirectoryName(Path.GetDirectoryName(audioFile.FilePath)) ??
                                       Path.GetDirectoryName(audioFile.FilePath) ??
                                       Directory.GetCurrentDirectory();

                (TranscriptionResult result, string jsonPath) = await ProcessTranscriptionWithJsonSaveAsync(
                    audioUrl, audioFile.FilePath, sessionFolderPath, numOfSpeakers);

                // Update audio file with transcription completion
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.TranscriptsDownloaded;
                audioFile.TranscriptId = result.id;
                audioFile.JsonFilePath = jsonPath; // Store path to JSON file

                // Build transcript from utterances with proper speaker mapping
                audioFile.TranscriptText = micAssignments != null && micAssignments.Any()
                    ? BuildTranscriptFromUtterances(result, micAssignments, audioFile.FileName)
                    : result.result?.transcription?.full_transcript ?? string.Empty;

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

    #region Transcription Management - DANGER ZONE
    /// <summary>
    /// Lists ALL transcriptions from Gladia account using V2 API.
    /// WARNING: This will retrieve ALL transcriptions associated with your API key.
    /// Use ListTranscriptionsAsync for safer pagination.
    /// </summary>
    public async Task<List<TranscriptionListItem>> ListAllTranscriptionsAsync(int limit = 100, int offset = 0)
    {
        try
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Gladia service is not configured with an API key");
            }

            _logger.LogWarning("LISTING ALL TRANSCRIPTIONS FROM GLADIA - This retrieves ALL items associated with your API key");

            // Use the correct V2 API endpoint for listing transcriptions
            string endpoint = $"/v2/pre-recorded?limit={limit}&offset={offset}";
            HttpResponseMessage response = await _httpClient.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to list transcriptions: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Failed to list transcriptions: {response.StatusCode} - {errorContent}");
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("List transcriptions response: {Response}", jsonResponse);

            TranscriptionListResponse? listResponse = JsonSerializer.Deserialize<TranscriptionListResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            _logger.LogInformation("Found {Count} transcriptions in current batch (limit: {Limit}, offset: {Offset})",
                listResponse?.transcriptions?.Count ?? 0, limit, offset);

            return listResponse?.transcriptions ?? new List<TranscriptionListItem>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing transcriptions from Gladia");
            throw;
        }
    }

    /// <summary>
    /// Deletes a single transcription from Gladia using V2 pre-recorded API.
    /// WARNING: This action is PERMANENT and cannot be undone.
    /// Uses the current recommended /v2/pre-recorded/{id} endpoint.
    /// </summary>
    public async Task<bool> DeleteTranscriptionAsync(string transcriptionId)
    {
        try
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Gladia service is not configured with an API key");
            }

            if (string.IsNullOrWhiteSpace(transcriptionId))
            {
                throw new ArgumentException("Transcription ID cannot be null or empty", nameof(transcriptionId));
            }

            _logger.LogWarning("DELETING TRANSCRIPTION {TranscriptionId} FROM GLADIA - This action is PERMANENT", transcriptionId);

            // Use the current V2 pre-recorded API endpoint for deleting transcriptions
            // This replaces the deprecated /v2/transcription/{id} endpoint
            HttpResponseMessage response = await _httpClient.DeleteAsync($"/v2/pre-recorded/{transcriptionId}");

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to delete transcription {TranscriptionId}: {StatusCode} - {Error}",
                    transcriptionId, response.StatusCode, errorContent);

                // Handle specific status codes
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Transcription {TranscriptionId} not found - may have been already deleted", transcriptionId);
                    return false;
                }

                // Return false for client errors (400, 401, 403, 404) but throw for server errors (500+)
                if ((int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"Server error deleting transcription: {response.StatusCode} - {errorContent}");
                }

                return false;
            }

            // Response 202 means deletion was accepted (async operation)
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                _logger.LogInformation("Successfully initiated deletion of transcription {TranscriptionId} (async operation)", transcriptionId);
            }
            else
            {
                _logger.LogInformation("Successfully deleted transcription {TranscriptionId}", transcriptionId);
            }

            return true;
        }
        catch (Exception ex) when (!(ex is HttpRequestException))
        {
            _logger.LogError(ex, "Error deleting transcription {TranscriptionId}", transcriptionId);
            throw;
        }
    }


    /// <summary>
    /// PURGES ALL transcriptions from your Gladia account.
    /// WARNING: This is EXTREMELY DANGEROUS and will delete ALL transcriptions permanently!
    /// This action CANNOT be undone!
    /// </summary>
    public async Task<PurgeResult> PurgeAllTranscriptionsAsync(Func<int, int, Task<bool>>? confirmationCallback = null)
    {
        PurgeResult result = new();

        try
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Gladia service is not configured with an API key");
            }

            _logger.LogWarning("INITIATING PURGE OF ALL GLADIA TRANSCRIPTIONS - THIS IS PERMANENT!");

            // First, get all transcriptions
            List<TranscriptionListItem> allTranscriptions = new();
            int offset = 0;
            const int batchSize = 100;
            bool hasMore = true;

            while (hasMore)
            {
                List<TranscriptionListItem> batch = await ListAllTranscriptionsAsync(batchSize, offset);
                if (batch.Any())
                {
                    allTranscriptions.AddRange(batch);
                    offset += batch.Count;
                    hasMore = batch.Count == batchSize;
                }
                else
                {
                    hasMore = false;
                }
            }

            result.TotalFound = allTranscriptions.Count;

            if (result.TotalFound == 0)
            {
                _logger.LogInformation("No transcriptions found to purge");
                return result;
            }

            // Require confirmation for safety
            if (confirmationCallback != null)
            {
                bool confirmed = await confirmationCallback(result.TotalFound, 0);
                if (!confirmed)
                {
                    result.WasCancelled = true;
                    _logger.LogInformation("Purge cancelled by user");
                    return result;
                }
            }

            // Delete each transcription
            foreach (TranscriptionListItem transcription in allTranscriptions)
            {
                try
                {
                    bool deleted = await DeleteTranscriptionAsync(transcription.id);
                    if (deleted)
                    {
                        result.DeletedIds.Add(transcription.id);
                        result.TotalDeleted++;
                    }
                    else
                    {
                        result.FailedIds.Add(transcription.id);
                        result.TotalFailed++;
                    }

                    // Brief delay to avoid rate limiting
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete transcription {TranscriptionId}", transcription.id);
                    result.FailedIds.Add(transcription.id);
                    result.TotalFailed++;
                }
            }

            _logger.LogWarning("PURGE COMPLETE: Deleted {Deleted} of {Total} transcriptions. Failed: {Failed}",
                result.TotalDeleted, result.TotalFound, result.TotalFailed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during purge operation");
            result.CriticalError = ex.Message;
            throw;
        }
    }

    #endregion
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

// Purge-related DTOs
public class TranscriptionListResponse
{
    public List<TranscriptionListItem> transcriptions { get; set; } = new();
    public int total { get; set; }
    public int limit { get; set; }
    public int offset { get; set; }
}

public class TranscriptionListItem
{
    public string id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public DateTime created_at { get; set; }
    public DateTime? completed_at { get; set; }
    public string? filename { get; set; }
    public double? duration { get; set; }
}

public class PurgeResult
{
    public int TotalFound { get; set; }
    public int TotalDeleted { get; set; }
    public int TotalFailed { get; set; }
    public List<string> DeletedIds { get; set; } = new();
    public List<string> FailedIds { get; set; } = new();
    public bool WasCancelled { get; set; }
    public string? CriticalError { get; set; }
}
