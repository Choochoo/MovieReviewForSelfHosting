using MovieReviewApp.Models;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Infrastructure.FileSystem;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    private async Task<string> ConvertToMp3Async(string inputPath, string outputPath, Action<string, int, int>? progressCallback = null)
    {
        try
        {
            _logger.LogInformation("Converting {InputFile} to MP3", Path.GetFileName(inputPath));

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{inputPath}\" -codec:a libmp3lame -b:a 192k -ar 44100 -ac 2 -af \"volume=1.5\" \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process();
            process.StartInfo = startInfo;
            process.Start();

            // Fake progress loader with reliable process monitoring
            var progress = 0;
            var maxWaitTime = TimeSpan.FromMinutes(10);
            var startTime = DateTime.UtcNow;

            // Progress loop using short WaitForExit calls
            while ((DateTime.UtcNow - startTime) < maxWaitTime)
            {
                // Check if process finished with a short timeout
                if (process.WaitForExit(1000))
                {
                    // Process finished
                    break;
                }

                // Update progress
                progress = Math.Min(97, progress + 3); // Cap at 97% until process finishes
                var progressMessage = $"Converting {Path.GetFileName(inputPath)} ({progress}%)";
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
            var finalProgressMessage = $"Converting {Path.GetFileName(inputPath)} (100%)";
            progressCallback?.Invoke(finalProgressMessage, 1, 1);
            _logger.LogDebug("MP3 Conversion - Converting {FileName}: {Progress}%",
                Path.GetFileName(inputPath), 100);

            if (process.ExitCode != 0)
            {
                throw new Exception($"FFmpeg conversion failed with exit code {process.ExitCode} for {Path.GetFileName(inputPath)}");
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

                // Convert to MP3 in the same session folder
                var mp3FileName = Path.ChangeExtension(Path.GetFileName(audioFile.FilePath), ".mp3");
                var mp3Path = Path.Combine(sessionFolderPath, mp3FileName);

                await ConvertToMp3Async(audioFile.FilePath, mp3Path, progressCallback);

                // Update audio file with MP3 details
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.PendingMp3;
                audioFile.ConvertedAt = DateTime.UtcNow;

                // Delete the original WAV file
                var originalWavPath = audioFile.FilePath;
                try
                {
                    if (File.Exists(originalWavPath) && originalWavPath != mp3Path)
                    {
                        File.Delete(originalWavPath);
                        _logger.LogInformation("Deleted original WAV file: {FilePath}", originalWavPath);
                    }
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Failed to delete original WAV file: {FilePath}", originalWavPath);
                }

                // Update FilePath to point to the new MP3 file
                audioFile.FilePath = mp3Path;
                // Update FileName to reflect the new MP3 extension
                audioFile.FileName = Path.GetFileName(mp3Path);

                results.Add((audioFile, true, null));
                _logger.LogInformation("Converted {FileName} to MP3: {Mp3Path}",
                    Path.GetFileName(audioFile.FilePath), mp3Path);
            }
            catch (Exception ex)
            {
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.Failed;
                audioFile.ConversionError = ex.Message;

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
        
        // Debug: Log all files and their status
        Console.WriteLine($"DEBUG GLADIA: UploadAllMp3sToGladiaAsync called with {audioFiles.Count} files:");
        foreach (var file in audioFiles)
        {
            Console.WriteLine($"  File: {file.FileName}, Status: {file.ProcessingStatus}, AudioUrl: {(string.IsNullOrEmpty(file.AudioUrl) ? "EMPTY" : "SET")}, Path: {file.FilePath}");
        }
        
        // Process all MP3 files that can be uploaded to Gladia and haven't been uploaded yet
        var mp3Files = audioFiles.Where(f => (f.ProcessingStatus == AudioProcessingStatus.PendingMp3 ||
                                             f.ProcessingStatus == AudioProcessingStatus.ProcessedMp3 ||
                                             f.ProcessingStatus == AudioProcessingStatus.FailedMp3 ||
                                             f.ProcessingStatus == AudioProcessingStatus.UploadingToGladia) &&
                                           string.IsNullOrEmpty(f.AudioUrl) &&
                                           f.FilePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)).ToList();
        
        Console.WriteLine($"DEBUG GLADIA: After filtering, {mp3Files.Count} files qualify for upload:");

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
                Console.WriteLine($"DEBUG GLADIA: Starting upload for {audioFile.FileName} (File {i + 1}/{totalFiles})");
                Console.WriteLine($"DEBUG GLADIA: File path: {audioFile.FilePath}");
                Console.WriteLine($"DEBUG GLADIA: File exists: {File.Exists(audioFile.FilePath)}");
                
                var audioUrl = await UploadSingleFileToGladiaAsync(audioFile.FilePath);
                Console.WriteLine($"DEBUG GLADIA: Upload successful for {audioFile.FileName}, got URL: {audioUrl}");

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
                        Console.WriteLine($"DEBUG GLADIA: Session state saved after uploading {audioFile.FileName}");
                        _logger.LogInformation("Session state saved after uploading {FileName}", Path.GetFileName(audioFile.FilePath));
                    }
                    catch (Exception saveEx)
                    {
                        Console.WriteLine($"DEBUG GLADIA: Failed to save session state after uploading {audioFile.FileName}: {saveEx.Message}");
                        _logger.LogWarning(saveEx, "Failed to save session state after uploading {FileName}", Path.GetFileName(audioFile.FilePath));
                    }
                }

                results.Add((audioFile, true, null));
                _logger.LogInformation("Uploaded {FileName} to Gladia: {AudioUrl}",
                    Path.GetFileName(audioFile.FilePath), audioUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG GLADIA: Upload failed for {audioFile.FileName}: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"DEBUG GLADIA: Full exception: {ex}");
                
                audioFile.ProcessingStatus = Models.AudioProcessingStatus.FailedMp3;
                audioFile.ConversionError = ex.Message;

                results.Add((audioFile, false, ex.Message));
                _logger.LogError(ex, "Failed to upload {FileName} to Gladia", Path.GetFileName(audioFile.FilePath));
            }
        }

        return results;
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

        var transcriptLines = new List<string>();
        var upperFileName = fileName.ToUpper();

        // Check if this is an individual mic file (MIC1.WAV, MIC2.WAV, etc.)
        var micMatch = System.Text.RegularExpressions.Regex.Match(upperFileName, @"^MIC(\d+)\.(?:WAV|MP3)$");
        if (micMatch.Success)
        {
            var fileBasedMicNumber = int.Parse(micMatch.Groups[1].Value); // 1-based from filename (MIC1.mp3 = 1)
            var micNumber = fileBasedMicNumber - 1; // Convert to 0-based for mic assignments lookup
            _logger.LogInformation("DETECTED INDIVIDUAL MIC FILE: {FileName} -> file number {FileNumber} -> internal mic number {MicNumber}", fileName, fileBasedMicNumber, micNumber);
            _logger.LogInformation("AVAILABLE MIC ASSIGNMENTS: {Assignments}", 
                string.Join(", ", micAssignments.Select(kvp => $"[{kvp.Key}]='{kvp.Value}'")));
            _logger.LogInformation("LOOKING FOR MIC ASSIGNMENT: micAssignments[{MicNumber}] (converted from file mic {FileNumber})", micNumber, fileBasedMicNumber);
            
            // For individual mic files, all utterances belong to the mic owner
            // The speaker ID in utterances is always 0 for individual files since it's just one person
            if (micAssignments.TryGetValue(micNumber, out var participantName) && !string.IsNullOrEmpty(participantName))
            {
                _logger.LogInformation("BUILDING TRANSCRIPT: Processing {UtteranceCount} utterances for {ParticipantName} from mic {MicNumber}", 
                    result.result.transcription.utterances.Count, participantName, micNumber);
                    
                foreach (var utterance in result.result.transcription.utterances)
                {
                    if (!string.IsNullOrWhiteSpace(utterance.text))
                    {
                        var transcriptLine = $"{participantName}: {utterance.text.Trim()}";
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
                foreach (var utterance in result.result.transcription.utterances)
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
                
                foreach (var utterance in result.result.transcription.utterances)
                {
                    if (!string.IsNullOrWhiteSpace(utterance.text))
                    {
                        // Map Gladia speaker number (0-based) to participant name using mic assignments
                        // Try to map speaker 0 -> mic 0, speaker 1 -> mic 1, etc. (corrected to 0-based)
                        var micNumber = utterance.speaker;
                        
                        if (micAssignments.TryGetValue(micNumber, out var participantName) && !string.IsNullOrEmpty(participantName))
                        {
                            transcriptLines.Add($"{participantName}: {utterance.text.Trim()}");
                            _logger.LogTrace("Mapped speaker {SpeakerNum} to {ParticipantName} in master mix", utterance.speaker, participantName);
                        }
                        else
                        {
                            // Fallback to generic speaker label if no mapping found
                            transcriptLines.Add($"Speaker {utterance.speaker + 1}: {utterance.text.Trim()}");
                            _logger.LogTrace("No mapping found for speaker {SpeakerNum}, using generic label", utterance.speaker);
                        }
                    }
                }
            }
            
            var uniqueSpeakers = result.result.transcription.utterances.Select(u => u.speaker).Distinct().Count();
            _logger.LogInformation("Built transcript for master/mix file {FileName} with {LineCount} lines from {SpeakerCount} detected speakers", 
                fileName, transcriptLines.Count, uniqueSpeakers);
        }

        var finalTranscript = string.Join("\n", transcriptLines);
        
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
        var analysisLines = finalTranscript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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
        var transcriptLines = new List<string>();
        const double TIMESTAMP_TOLERANCE = 1.0; // Allow 1 second tolerance for timestamp matching
        
        _logger.LogInformation("Building timestamp-based transcript from master mix with {UtteranceCount} utterances", 
            masterMixResult.result.transcription.utterances.Count);

        foreach (var masterUtterance in masterMixResult.result.transcription.utterances)
        {
            if (string.IsNullOrWhiteSpace(masterUtterance.text))
                continue;

            string? bestMatchSpeaker = null;
            double bestMatchScore = 0;
            string? bestMatchText = null;

            // Check each individual mic file to see which participant's timing best matches this utterance
            foreach (var (micResult, participantName) in individualMicData)
            {
                if (micResult?.result?.transcription?.utterances == null)
                    continue;

                // Find the best matching utterance from this mic based on timestamp overlap
                foreach (var micUtterance in micResult.result.transcription.utterances)
                {
                    if (string.IsNullOrWhiteSpace(micUtterance.text))
                        continue;

                    // Calculate overlap between master utterance and mic utterance
                    var overlapStart = Math.Max(masterUtterance.start, micUtterance.start);
                    var overlapEnd = Math.Min(masterUtterance.end, micUtterance.end);
                    var overlap = Math.Max(0, overlapEnd - overlapStart);
                    
                    var masterDuration = masterUtterance.end - masterUtterance.start;
                    var micDuration = micUtterance.end - micUtterance.start;
                    
                    // Calculate overlap percentage relative to both utterances
                    var overlapScore = overlap / Math.Max(masterDuration, micDuration);
                    
                    // Also consider text similarity (simple approach)
                    var textSimilarity = CalculateTextSimilarity(masterUtterance.text, micUtterance.text);
                    var combinedScore = (overlapScore * 0.7) + (textSimilarity * 0.3);

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
                    masterUtterance.start, masterUtterance.end, bestMatchSpeaker, bestMatchScore);
            }
            else
            {
                // No good match found, use generic speaker label
                transcriptLines.Add($"Unknown Speaker: {masterUtterance.text.Trim()}");
                _logger.LogTrace("No speaker match found for master utterance [{Start:F1}s-{End:F1}s]: {Text}", 
                    masterUtterance.start, masterUtterance.end, masterUtterance.text.Substring(0, Math.Min(50, masterUtterance.text.Length)));
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

        var words1 = text1.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        
        if (words1.Count == 0 || words2.Count == 0)
            return 0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();
        
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
                var originalLength = result.Length;
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\bSpeaker \d+:", $"{participantName}:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\bspeaker \d+:", $"{participantName}:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\[Speaker \d+\]:", $"[{participantName}]:");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\(Speaker \d+\):", $"({participantName}):");
                // Also handle variations without colons
                result = System.Text.RegularExpressions.Regex.Replace(result, @"^Speaker \d+\s", $"{participantName}: ", System.Text.RegularExpressions.RegexOptions.Multiline);
                result = System.Text.RegularExpressions.Regex.Replace(result, @"^speaker \d+\s", $"{participantName}: ", System.Text.RegularExpressions.RegexOptions.Multiline);
                
                var replacedChars = originalLength - result.Length;
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
            var replacementCount = 0;
            // Map Speaker 1, Speaker 2, etc. to actual names based on mic assignments
            for (int i = 1; i <= 10; i++) // Support up to 10 speakers
            {
                if (micAssignments.TryGetValue(i, out var participantName) && !string.IsNullOrEmpty(participantName))
                {
                    var patterns = new[]
                    {
                        ($"Speaker {i}:", $"{participantName}:"),
                        ($"Speaker {i} :", $"{participantName}:"),
                        ($"speaker {i}:", $"{participantName}:"),
                        ($"speaker {i} :", $"{participantName}:"),
                        ($"[Speaker {i}]:", $"[{participantName}]:"),
                        ($"(Speaker {i}):", $"({participantName}):")
                    };

                    foreach (var (oldPattern, newPattern) in patterns)
                    {
                        var countBefore = result.Length;
                        result = result.Replace(oldPattern, newPattern);
                        if (result.Length != countBefore)
                        {
                            var occurrences = (countBefore - result.Length) / (oldPattern.Length - newPattern.Length);
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
    private async Task<string> UploadSingleFileToGladiaAsync(string filePath)
    {
        Console.WriteLine($"DEBUG GLADIA: UploadSingleFileToGladiaAsync called for: {filePath}");
        Console.WriteLine($"DEBUG GLADIA: IsConfigured: {IsConfigured}");
        Console.WriteLine($"DEBUG GLADIA: API key present: {!string.IsNullOrEmpty(_apiKey)}");
        
        // Log configuration status first
        LogConfigurationStatus();
        
        const int maxRetries = 3;
        const int baseDelayMs = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Console.WriteLine($"DEBUG GLADIA: Upload attempt {attempt}/{maxRetries} for {Path.GetFileName(filePath)}");
                _logger.LogInformation("Upload attempt {Attempt}/{MaxRetries} for {FileName}",
                    attempt, maxRetries, Path.GetFileName(filePath));

                return await UploadSingleFileToGladiaAsyncInternal(filePath);
            }
            catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
            {
                var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                Console.WriteLine($"DEBUG GLADIA: Upload attempt {attempt} failed for {Path.GetFileName(filePath)}, retrying in {delay}ms: {ex.Message}");
                _logger.LogWarning("Upload attempt {Attempt} failed for {FileName}, retrying in {Delay}ms: {Error}",
                    attempt, Path.GetFileName(filePath), delay, ex.Message);

                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG GLADIA: Upload attempt {attempt} failed with non-retryable exception: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        // Final attempt without retry wrapper
        Console.WriteLine($"DEBUG GLADIA: Final upload attempt for {Path.GetFileName(filePath)}");
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
        Console.WriteLine($"DEBUG GLADIA: Starting upload for file: {filePath}");
        
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"DEBUG GLADIA ERROR: File not found: {filePath}");
            throw new FileNotFoundException($"Audio file not found: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        Console.WriteLine($"DEBUG GLADIA: File info - Size: {fileInfo.Length:N0} bytes, Extension: {extension}");
        _logger.LogInformation("Uploading file {FileName} ({FileSize:N0} bytes) to Gladia",
            Path.GetFileName(filePath), fileInfo.Length);

        // Check API key availability
        Console.WriteLine($"DEBUG GLADIA: API Key configured: {!string.IsNullOrEmpty(_apiKey)}");
        Console.WriteLine($"DEBUG GLADIA: API Key length: {_apiKey?.Length ?? 0}");
        Console.WriteLine($"DEBUG GLADIA: Base URL: {_baseUrl}");
        Console.WriteLine($"DEBUG GLADIA: HttpClient BaseAddress: {_httpClient.BaseAddress}");

        // Check headers
        Console.WriteLine($"DEBUG GLADIA: Request headers count: {_httpClient.DefaultRequestHeaders.Count()}");
        foreach (var header in _httpClient.DefaultRequestHeaders)
        {
            var maskedValue = header.Key.ToLower().Contains("key") && header.Value.Any() 
                ? $"{header.Value.First().Substring(0, Math.Min(10, header.Value.First().Length))}..." 
                : string.Join(", ", header.Value);
            Console.WriteLine($"DEBUG GLADIA: Header - {header.Key}: {maskedValue}");
        }

        using var form = new MultipartFormDataContent();

        // Use streaming instead of loading entire file into memory
        Console.WriteLine($"DEBUG GLADIA: Opening file stream for {Path.GetFileName(filePath)}");
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

        Console.WriteLine($"DEBUG GLADIA: Setting content type: {contentType}");
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        
        // Create custom filename with movie name and mic identifier
        var customFilename = CreateCustomFilename(filePath);
        Console.WriteLine($"DEBUG GLADIA: Custom filename: {customFilename}");
        form.Add(fileContent, "audio", customFilename);

        Console.WriteLine($"DEBUG GLADIA: Posting to endpoint: /v2/upload");
        Console.WriteLine($"DEBUG GLADIA: Full URL will be: {_httpClient.BaseAddress}/v2/upload");

        try
        {
            var response = await _httpClient.PostAsync("/v2/upload", form);
            Console.WriteLine($"DEBUG GLADIA: Response status: {response.StatusCode}");
            Console.WriteLine($"DEBUG GLADIA: Response headers count: {response.Headers.Count()}");
            
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"DEBUG GLADIA: Response Header - {header.Key}: {string.Join(", ", header.Value)}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"DEBUG GLADIA ERROR: Status {response.StatusCode}, Content: {errorContent}");
                
                // Log detailed error info
                Console.WriteLine($"DEBUG GLADIA ERROR: Reason phrase: {response.ReasonPhrase}");
                if (response.Content.Headers.Any())
                {
                    Console.WriteLine($"DEBUG GLADIA ERROR: Content headers:");
                    foreach (var header in response.Content.Headers)
                    {
                        Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
                    }
                }
                
                throw new Exception($"Gladia upload failed with status {response.StatusCode}: {errorContent}");
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"DEBUG GLADIA: Success response length: {content.Length}");
            Console.WriteLine($"DEBUG GLADIA: Response content: {content}");
            
            var uploadResult = JsonSerializer.Deserialize<UploadResponse>(content);
            Console.WriteLine($"DEBUG GLADIA: Deserialized audio_url: {uploadResult?.audio_url}");

            if (uploadResult?.audio_url == null)
            {
                Console.WriteLine($"DEBUG GLADIA ERROR: Missing audio_url in response: {content}");
                throw new Exception($"Gladia upload response missing audio_url: {content}");
            }

            _logger.LogInformation("Successfully uploaded {FileName} to Gladia with URL: {AudioUrl}",
                Path.GetFileName(filePath), uploadResult.audio_url);

            Console.WriteLine($"DEBUG GLADIA: Upload successful, returning URL: {uploadResult.audio_url}");
            return uploadResult.audio_url;
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"DEBUG GLADIA ERROR: HttpRequestException - {httpEx.Message}");
            Console.WriteLine($"DEBUG GLADIA ERROR: Inner exception: {httpEx.InnerException?.Message}");
            throw;
        }
        catch (TaskCanceledException tcEx)
        {
            Console.WriteLine($"DEBUG GLADIA ERROR: TaskCanceledException (timeout?) - {tcEx.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUG GLADIA ERROR: General exception - {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"DEBUG GLADIA ERROR: Stack trace: {ex.StackTrace}");
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

            var response = await _httpClient.PostAsync("/v2/upload", form);

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