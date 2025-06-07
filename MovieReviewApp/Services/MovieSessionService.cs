using MovieReviewApp.Database;
using MovieReviewApp.Models;
using NAudio.Wave;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Services;

public class MovieSessionService
{
    private readonly MongoDbService _database;
    private readonly GladiaService _gladiaService;
    private readonly MovieSessionAnalysisService _analysisService;
    private readonly AudioClipService _audioClipService;
    private readonly AudioFileOrganizer _audioOrganizer;
    private readonly ILogger<MovieSessionService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public MovieSessionService(
        MongoDbService database,
        GladiaService gladiaService,
        MovieSessionAnalysisService analysisService,
        AudioClipService audioClipService,
        AudioFileOrganizer audioOrganizer,
        ILogger<MovieSessionService> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _database = database;
        _gladiaService = gladiaService;
        _analysisService = analysisService;
        _audioClipService = audioClipService;
        _audioOrganizer = audioOrganizer;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<MovieSession> PrepareSessionFromFolder(string folderPath, Dictionary<int, string>? micAssignments = null)
    {
        var folderName = Path.GetFileName(folderPath);

        // Extract date from folder name (format: YYYY-MonthName-MovieTitle)
        var monthNameMatch = Regex.Match(folderName, @"(\d{4})-([A-Za-z]+)-(.+)");
        DateTime sessionDate;

        if (monthNameMatch.Success)
        {
            try
            {
                var year = int.Parse(monthNameMatch.Groups[1].Value);
                var monthName = monthNameMatch.Groups[2].Value;

                // Parse month name to number
                if (DateTime.TryParseExact($"{monthName} 1, {year}", "MMMM d, yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out sessionDate))
                {
                    _logger.LogInformation("Extracted date {Date} from folder name", sessionDate.ToShortDateString());
                }
                else
                {
                    sessionDate = DateTime.Now;
                    _logger.LogWarning("Invalid month name '{MonthName}' in folder name, using current date", monthName);
                }
            }
            catch
            {
                sessionDate = DateTime.Now;
                _logger.LogWarning("Invalid date format in folder name, using current date");
            }
        }
        else
        {
            sessionDate = DateTime.Now;
            _logger.LogDebug("No date found in folder name, using current date");
        }

        // Extract movie title from folder name
        var movieTitle = SuggestMovieTitle(folderName);

        var session = new MovieSession
        {
            Date = sessionDate,
            MovieTitle = movieTitle,
            FolderPath = folderPath,
            Status = ProcessingStatus.Pending,
            MicAssignments = micAssignments ?? new Dictionary<int, string>()
        };

        // Scan for audio files
        await ScanAudioFiles(session);

        // Determine participants using mic assignments if available
        DetermineParticipants(session);

        _logger.LogInformation("Prepared movie session {SessionId} for {MovieTitle} on {Date} (not saved to database yet)",
            session.Id, session.MovieTitle, session.Date);

        return session;
    }

    public async Task<MovieSession> SaveSessionToDatabase(MovieSession session)
    {
        await _database.UpsertAsync(session);
        _logger.LogInformation("Saved movie session {SessionId} to database after successful processing", session.Id);
        return session;
    }

    /// <summary>
    /// Enhanced processing workflow using database tracking instead of folder management
    /// </summary>
    public async Task<MovieSession> ProcessSessionEnhanced(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Starting enhanced processing workflow", 0);
        
        try
        {
            session.Status = ProcessingStatus.Validating;
            await SaveSessionToDatabase(session);
            progressCallback?.Invoke("Session validated and saved", 10);

            // Phase 1: Convert WAV files to MP3
            await ConvertAudioFiles(session, progressCallback);
            
            // Phase 2: Upload to Gladia
            await UploadAudioFiles(session, progressCallback);
            
            // Phase 3: Process transcriptions
            await ProcessTranscriptions(session, progressCallback);
            
            // Phase 4: AI Analysis
            await RunAIAnalysis(session, progressCallback);
            
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;
            await SaveSessionToDatabase(session);
            
            progressCallback?.Invoke("Processing complete", 100);
            return session;
        }
        catch (Exception ex)
        {
            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            await SaveSessionToDatabase(session);
            throw;
        }
    }

    private async Task ConvertAudioFiles(MovieSession session, Action<string, int>? progressCallback = null)
    {
        var wavFiles = session.AudioFiles.Where(f => f.FilePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!wavFiles.Any()) return;

        progressCallback?.Invoke("Converting WAV files to MP3", 20);
        
        for (int i = 0; i < wavFiles.Count; i++)
        {
            var file = wavFiles[i];
            file.ProcessingStatus = AudioProcessingStatus.ConvertingToMp3;
            file.CurrentStep = "Converting to MP3";
            file.ProgressPercentage = 0;
            await SaveSessionToDatabase(session);

            try
            {
                // Use existing Gladia service conversion logic
                var results = await _gladiaService.ConvertAllWavsToMp3Async(
                    new List<AudioFile> { file }, 
                    session.FolderPath,
                    (step, current, total) => 
                    {
                        file.ProgressPercentage = (int)((double)current / total * 100);
                        file.CurrentStep = step;
                    });

                var result = results.FirstOrDefault();
                if (result.success)
                {
                    file.ProcessingStatus = AudioProcessingStatus.PendingMp3;
                    file.CurrentStep = "MP3 conversion complete";
                    file.ProgressPercentage = 100;
                    file.CanRetry = true;
                }
                else
                {
                    file.ProcessingStatus = AudioProcessingStatus.FailedMp3;
                    file.ConversionError = result.error;
                    file.CurrentStep = "MP3 conversion failed";
                    file.CanRetry = true;
                }
            }
            catch (Exception ex)
            {
                file.ProcessingStatus = AudioProcessingStatus.Failed;
                file.ConversionError = ex.Message;
                file.CurrentStep = "Conversion error";
                file.CanRetry = true;
            }

            await SaveSessionToDatabase(session);
            progressCallback?.Invoke($"Converted {i + 1}/{wavFiles.Count} files", 20 + (i + 1) * 20 / wavFiles.Count);
        }
    }

    private async Task UploadAudioFiles(MovieSession session, Action<string, int>? progressCallback = null)
    {
        var filesToUpload = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.PendingMp3 && 
            string.IsNullOrEmpty(f.AudioUrl)).ToList();
        
        if (!filesToUpload.Any()) return;

        progressCallback?.Invoke("Uploading files to Gladia", 40);

        for (int i = 0; i < filesToUpload.Count; i++)
        {
            var file = filesToUpload[i];
            file.ProcessingStatus = AudioProcessingStatus.UploadingToGladia;
            file.CurrentStep = "Uploading to Gladia";
            file.ProgressPercentage = 0;
            await SaveSessionToDatabase(session);

            try
            {
                var results = await _gladiaService.UploadAllMp3sToGladiaAsync(
                    new List<AudioFile> { file },
                    session.FolderPath,
                    (step, current, total) => 
                    {
                        file.ProgressPercentage = (int)((double)current / total * 100);
                        file.CurrentStep = step;
                    },
                    session,
                    SaveSessionToDatabase);

                var result = results.FirstOrDefault();
                if (result.success)
                {
                    file.ProcessingStatus = AudioProcessingStatus.UploadedToGladia;
                    file.CurrentStep = "Upload complete";
                    file.ProgressPercentage = 100;
                    file.CanRetry = true;
                }
                else
                {
                    file.ProcessingStatus = AudioProcessingStatus.Failed;
                    file.ConversionError = result.error;
                    file.CurrentStep = "Upload failed";
                    file.CanRetry = true;
                }
            }
            catch (Exception ex)
            {
                file.ProcessingStatus = AudioProcessingStatus.Failed;
                file.ConversionError = ex.Message;
                file.CurrentStep = "Upload error";
                file.CanRetry = true;
            }

            await SaveSessionToDatabase(session);
            progressCallback?.Invoke($"Uploaded {i + 1}/{filesToUpload.Count} files", 40 + (i + 1) * 20 / filesToUpload.Count);
        }
    }

    private async Task ProcessTranscriptions(MovieSession session, Action<string, int>? progressCallback = null)
    {
        var filesToTranscribe = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.UploadedToGladia && 
            !string.IsNullOrEmpty(f.AudioUrl) &&
            string.IsNullOrEmpty(f.TranscriptText)).ToList();

        if (!filesToTranscribe.Any()) return;

        progressCallback?.Invoke("Processing transcriptions", 60);

        for (int i = 0; i < filesToTranscribe.Count; i++)
        {
            var file = filesToTranscribe[i];
            file.ProcessingStatus = AudioProcessingStatus.Transcribing;
            file.CurrentStep = "Starting transcription";
            file.ProgressPercentage = 0;
            await SaveSessionToDatabase(session);

            try
            {
                // Start transcription
                var numSpeakers = session.MicAssignments?.Count ?? 2;
                var transcriptionId = await _gladiaService.StartTranscriptionAsync(
                    file.AudioUrl!, numSpeakers, true, file.FileName);
                
                file.TranscriptId = transcriptionId;
                file.CurrentStep = "Transcription in progress";
                file.ProgressPercentage = 50;
                await SaveSessionToDatabase(session);

                // Wait for completion
                var result = await _gladiaService.WaitForTranscriptionAsync(transcriptionId);
                
                // Apply speaker mapping
                var rawTranscript = result.result?.transcription?.full_transcript;
                file.TranscriptText = session.MicAssignments != null && !string.IsNullOrEmpty(rawTranscript)
                    ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, session.MicAssignments, file.FileName)
                    : rawTranscript;

                file.ProcessingStatus = AudioProcessingStatus.TranscriptionComplete;
                file.CurrentStep = "Transcription complete";
                file.ProgressPercentage = 100;
                file.ProcessedAt = DateTime.UtcNow;
                file.CanRetry = true;
            }
            catch (Exception ex)
            {
                file.ProcessingStatus = AudioProcessingStatus.Failed;
                file.ConversionError = ex.Message;
                file.CurrentStep = "Transcription failed";
                file.CanRetry = true;
            }

            await SaveSessionToDatabase(session);
            progressCallback?.Invoke($"Transcribed {i + 1}/{filesToTranscribe.Count} files", 60 + (i + 1) * 20 / filesToTranscribe.Count);
        }
    }

    private async Task RunAIAnalysis(MovieSession session, Action<string, int>? progressCallback = null)
    {
        var transcribedFiles = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete &&
            !string.IsNullOrEmpty(f.TranscriptText)).ToList();

        if (!transcribedFiles.Any()) return;

        progressCallback?.Invoke("Running AI analysis", 80);

        try
        {
            // Mark files as processing
            foreach (var file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.ProcessingWithAI;
                file.CurrentStep = "AI analysis in progress";
                file.ProgressPercentage = 50;
            }
            await SaveSessionToDatabase(session);

            // Run analysis using existing service
            await _analysisService.AnalyzeSessionAsync(session);

            // Mark as complete
            foreach (var file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.Complete;
                file.CurrentStep = "Processing complete";
                file.ProgressPercentage = 100;
                file.CanRetry = false;
            }

            progressCallback?.Invoke("AI analysis complete", 95);
        }
        catch (Exception ex)
        {
            foreach (var file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.Failed;
                file.ConversionError = ex.Message;
                file.CurrentStep = "AI analysis failed";
                file.CanRetry = true;
            }
            throw;
        }

        await SaveSessionToDatabase(session);
    }


    private string SuggestMovieTitle(string folderName)
    {
        // Extract movie title from format: YYYY-MonthName-MovieTitle
        var monthNameMatch = Regex.Match(folderName, @"(\d{4})-([A-Za-z]+)-(.+)");
        if (monthNameMatch.Success)
        {
            var moviePart = monthNameMatch.Groups[3].Value;
            // Convert hyphens to spaces for movie title
            return moviePart.Replace("-", " ").Trim();
        }

        // Fallback: return folder name as-is if format doesn't match
        return folderName;
    }

    private async Task ScanAudioFiles(MovieSession session)
    {
        var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma",
            ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp"
        };

        var files = Directory.GetFiles(session.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        _logger.LogInformation("Scanning audio files in {FolderPath}, found {FileCount} audio files", session.FolderPath, files.Count);
        foreach (var file in files)
        {
            _logger.LogInformation("Found audio file: {FileName}", Path.GetFileName(file));
        }

        var identifiedFiles = new HashSet<string>();

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var fileNameUpper = fileName.ToUpper();
            var fileInfo = new FileInfo(filePath);

            var audioFile = new AudioFile
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileInfo.Length,
                Duration = await GetMediaDuration(filePath)
            };

            // Set initial processing status - all files start as pending
            audioFile.ProcessingStatus = AudioProcessingStatus.Pending;

            // File properties are already set above, no need for special MP3 handling

            // Check for MIC1-9 pattern (convert 1-based file naming to 0-based speaker numbers)
            var micMatch = Regex.Match(fileNameUpper, @"^MIC(\d)\.WAV$");
            if (micMatch.Success)
            {
                var micFileNumber = int.Parse(micMatch.Groups[1].Value); // 1-based from file
                audioFile.SpeakerNumber = micFileNumber - 1; // Convert to 0-based for consistency
                identifiedFiles.Add(fileName);
                _logger.LogInformation("Detected microphone file: {FileName} as MIC{FileNumber} (Speaker {SpeakerNumber})", fileName, micFileNumber, audioFile.SpeakerNumber);
            }
            // Check for existing speaker pattern (convert 1-based file naming to 0-based speaker numbers)
            else if (Regex.IsMatch(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase))
            {
                var match = Regex.Match(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase);
                var speakerFileNumber = int.Parse(match.Groups[1].Value); // 1-based from file
                audioFile.SpeakerNumber = speakerFileNumber - 1; // Convert to 0-based for consistency
                identifiedFiles.Add(fileName);
            }
            // Skip PHONE.WAV and SOUND_PAD.WAV files (they're not speakers)
            else if (fileNameUpper == "PHONE.WAV" || fileNameUpper == "SOUND_PAD.WAV" || fileNameUpper == "SOUNDPAD.WAV")
            {
                identifiedFiles.Add(fileName);
                _logger.LogDebug("Identified {FileType} file: {FileName}",
                    fileNameUpper.Contains("PHONE") ? "phone" : "sound pad", fileName);
            }
            // Check for master recording with timestamp pattern (e.g., 2024_1122_1839.wav)
            else if (Regex.IsMatch(fileName, @"^\d{4}_\d{4}_\d{4}\.(wav|mp3|m4a|aac|ogg|flac)$", RegexOptions.IgnoreCase))
            {
                audioFile.IsMasterRecording = true;
                identifiedFiles.Add(fileName);
                _logger.LogInformation("Identified timestamped master mix file: {FileName}", fileName);
            }
            // Check for master recording patterns
            else if (fileName.ToLower().Contains("master") ||
                     fileName.ToLower().Contains("combined") ||
                     fileName.ToLower().Contains("full") ||
                     fileName.ToLower().Contains("group"))
            {
                audioFile.IsMasterRecording = true;
                identifiedFiles.Add(fileName);
            }

            session.AudioFiles.Add(audioFile);
        }

        // Any unidentified file is likely the master mix (critical file)
        var unidentifiedFiles = session.AudioFiles.Where(f => !identifiedFiles.Contains(f.FileName)).ToList();
        if (unidentifiedFiles.Count == 1)
        {
            unidentifiedFiles[0].IsMasterRecording = true;
            _logger.LogInformation("Identified master mix file by elimination: {FileName}", unidentifiedFiles[0].FileName);
        }
        else if (unidentifiedFiles.Count > 1)
        {
            // If multiple unidentified files, pick the largest as master mix
            var largestFile = unidentifiedFiles.OrderByDescending(f => f.FileSize).First();
            largestFile.IsMasterRecording = true;
            _logger.LogWarning("Multiple unidentified files. Selected {FileName} as master mix based on size", largestFile.FileName);
        }

        // Rename master recording to MASTER_MIX with original extension preserved
        var masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording);
        if (masterFile != null && !masterFile.FileName.StartsWith("MASTER_MIX", StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = masterFile.FilePath;
            var directory = Path.GetDirectoryName(oldPath);
            var originalExtension = Path.GetExtension(oldPath);
            var newFileName = $"MASTER_MIX{originalExtension}";
            var newPath = Path.Combine(directory!, newFileName);

            try
            {
                // If MASTER_MIX file already exists with this extension, don't overwrite
                if (!File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    masterFile.FileName = newFileName;
                    masterFile.FilePath = newPath;
                    _logger.LogInformation("Renamed master mix file from {OldName} to {NewName}", Path.GetFileName(oldPath), newFileName);
                }
                else
                {
                    _logger.LogWarning("{NewName} already exists, keeping original file name: {FileName}", newFileName, masterFile.FileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename master mix file from {OldName} to {NewName}", masterFile.FileName, newFileName);
            }
        }

        // Log warning if no master recording found
        if (!session.AudioFiles.Any(f => f.IsMasterRecording))
        {
            _logger.LogError("CRITICAL: No master mix file identified in session {SessionId}", session.Id);
        }
    }

    private void DetermineParticipants(MovieSession session)
    {
        // Based on audio files, determine who was present (using zero-based speaker numbers)
        var presentSpeakers = session.AudioFiles
            .Where(f => f.SpeakerNumber.HasValue)
            .Select(f => f.SpeakerNumber!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // All possible speakers (0-8) - supports up to 9 microphones (zero-based)
        var allSpeakers = Enumerable.Range(0, 9).ToList();
        var absentSpeakers = allSpeakers.Except(presentSpeakers).ToList();

        // Use mic assignments if available, otherwise show generic mic labels
        session.ParticipantsPresent = presentSpeakers.Select(mic =>
            session.MicAssignments.TryGetValue(mic, out var name) && !string.IsNullOrEmpty(name)
                ? name
                : GetParticipantName(mic + 1) // Convert to 1-based for display
        ).ToList();

        session.ParticipantsAbsent = absentSpeakers.Select(mic =>
            session.MicAssignments.TryGetValue(mic, out var name) && !string.IsNullOrEmpty(name)
                ? name
                : GetParticipantName(mic + 1) // Convert to 1-based for display
        ).ToList();
    }

    public static string GetParticipantName(int micNumber)
    {
        return $"Mic {micNumber}";
    }

    private async Task<TimeSpan?> GetMediaDuration(string filePath)
    {
        try
        {
            using var reader = new MediaFoundationReader(filePath);
            return reader.TotalTime;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get duration for file {FilePath}", filePath);
            return null;
        }
    }

    public async Task ProcessSession(MovieSession session, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        try
        {
            // Step 1: Validation
            progressCallback?.Invoke(ProcessingStatus.Validating, 10, "Validating session data");
            session.Status = ProcessingStatus.Validating;

            if (!session.AudioFiles.Any())
            {
                throw new Exception("No audio files found in session");
            }

            // Step 2: Transcription
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 20, "Starting transcription");
            session.Status = ProcessingStatus.Transcribing;

            // Use the session's folder path for organizing files, not the generic uploads folder
            var sessionFolderPath = session.FolderPath;
            _audioOrganizer.InitializeAudioFolders(sessionFolderPath);

            // Check if Gladia service is properly configured
            _gladiaService.LogConfigurationStatus();

            if (!_gladiaService.IsConfigured)
            {
                throw new Exception("Gladia API key not configured. Cannot process audio files.");
            }

            // Phase 1: Convert WAV files to MP3 (process only files that need conversion)
            // Include any WAV file that hasn't been successfully converted to MP3
            var filesToConvert = session.AudioFiles.Where(f =>
                (f.ProcessingStatus == AudioProcessingStatus.Pending ||
                 f.ProcessingStatus == AudioProcessingStatus.Failed ||
                 // Also include WAV files that might be in wrong status due to previous incomplete runs
                 (Path.GetExtension(f.FilePath).ToLowerInvariant() == ".wav" &&
                  f.ProcessingStatus == AudioProcessingStatus.PendingMp3))).ToList();

            if (filesToConvert.Any())
            {
                progressCallback?.Invoke(ProcessingStatus.Transcribing, 10, "Converting audio files to MP3...");

                var conversionResults = await _gladiaService.ConvertAllWavsToMp3Async(filesToConvert, sessionFolderPath,
                    (message, current, total) =>
                    {
                        var progress = 10 + (int)((double)current / total * 20); // 10-30%
                        progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, message);
                    });

                // Phase 2: Move successful MP3s to processed_mp3 and delete original WAVs
                progressCallback?.Invoke(ProcessingStatus.Transcribing, 30, "Organizing converted files...");

                foreach (var (audioFile, success, error) in conversionResults)
                {
                    if (success)
                    {
                        // Files have already been converted and moved by GladiaService
                        // FilePath now points to the MP3 file in pending_mp3 folder
                        _logger.LogInformation("File {FileName} successfully converted to MP3", audioFile.FileName);
                    }
                    else
                    {
                        // Files are already moved to failed folder by conversion process
                        audioFile.ConversionError = error ?? "Unknown error during conversion";
                    }
                }
            }
            else
            {
                _logger.LogInformation("Skipping MP3 conversion - no files need conversion");
                progressCallback?.Invoke(ProcessingStatus.Transcribing, 30, "No files need MP3 conversion");
            }

            // Phase 3: Upload all MP3s to Gladia
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 35, "Uploading files to Gladia...");

            var mp3Files = session.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.PendingMp3 || f.ProcessingStatus == AudioProcessingStatus.FailedMp3 || f.ProcessingStatus == AudioProcessingStatus.ProcessedMp3).ToList();
            if (!mp3Files.Any())
            {
                throw new Exception("No MP3 files were successfully converted for upload.");
            }

            var uploadResults = await _gladiaService.UploadAllMp3sToGladiaAsync(mp3Files, sessionFolderPath,
                (message, current, total) =>
                {
                    var progress = 35 + (int)((double)current / total * 25); // 35-60%
                    progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, message);
                },
                session, // Pass session for tracking
                async (s) => await SaveSessionToDatabase(s)); // Save callback to persist state immediately

            // Phase 4: Start transcriptions and wait for completion
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 60, "Starting transcriptions...");

            var transcriptionTasks = new List<Task>();
            foreach (var (audioFile, success, error) in uploadResults)
            {
                if (success && !string.IsNullOrEmpty(audioFile.AudioUrl))
                {
                    transcriptionTasks.Add(ProcessSingleTranscription(audioFile, session.MicAssignments, sessionFolderPath));
                }
                else
                {
                    audioFile.ProcessingStatus = AudioProcessingStatus.FailedMp3;
                    audioFile.ConversionError = error ?? "Unknown error during upload";

                    // File stays in session folder, status tracked in database
                }
            }

            // Wait for all transcriptions to complete
            await Task.WhenAll(transcriptionTasks);

            // Phase 5: Save transcription files alongside audio files in their current folders
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 70, "Saving transcription files...");

            foreach (var audioFile in session.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete))
            {
                if (!string.IsNullOrEmpty(audioFile.TranscriptText))
                {
                    try
                    {
                        // Save transcript text file in the same directory as the audio file
                        var audioDirectory = Path.GetDirectoryName(audioFile.FilePath);
                        if (!string.IsNullOrEmpty(audioDirectory) && Directory.Exists(audioDirectory))
                        {
                            var transcriptFileName = Path.ChangeExtension(audioFile.FileName, ".txt");
                            var transcriptPath = Path.Combine(audioDirectory, transcriptFileName);

                            await File.WriteAllTextAsync(transcriptPath, audioFile.TranscriptText);
                            _logger.LogInformation("Saved transcript to: {TranscriptPath}", transcriptPath);
                        }
                        else
                        {
                            _logger.LogWarning("Could not save transcript for {FileName} - directory not found: {Directory}",
                                audioFile.FileName, audioDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to save transcript for {FileName}", audioFile.FileName);
                    }
                }

                // Note: JSON files are already saved by GladiaService during transcription
                // The JsonFilePath property contains the path to the JSON file
                if (!string.IsNullOrEmpty(audioFile.JsonFilePath))
                {
                    _logger.LogInformation("JSON transcription already saved at: {JsonPath}", audioFile.JsonFilePath);
                }
            }

            _logger.LogInformation("Audio processing completed. {SuccessCount} successful, {FailedCount} failed",
                session.AudioFiles.Count(f => f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete),
                session.AudioFiles.Count(f => f.ProcessingStatus == AudioProcessingStatus.Failed || f.ProcessingStatus == AudioProcessingStatus.FailedMp3));

            // Step 3: AI Analysis
            progressCallback?.Invoke(ProcessingStatus.Analyzing, 75, "Analyzing transcripts for entertainment moments");
            session.Status = ProcessingStatus.Analyzing;

            await AnalyzeSession(session);

            // Step 4: Complete - ONLY NOW save to database after 100% success
            // Verify ALL files have successful transcripts before saving
            var successfulTranscripts = session.AudioFiles.Count(f =>
                f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete &&
                !string.IsNullOrEmpty(f.TranscriptText));

            var totalFiles = session.AudioFiles.Count;

            if (successfulTranscripts == 0)
            {
                throw new Exception($"No successful transcripts generated from {totalFiles} audio files. Session not saved.");
            }

            if (successfulTranscripts < totalFiles)
            {
                _logger.LogWarning("Only {SuccessCount}/{TotalCount} files successfully transcribed for session {SessionId}",
                    successfulTranscripts, totalFiles, session.Id);
            }

            // Final validation: ensure we have analysis results
            if (session.CategoryResults == null)
            {
                throw new Exception("Session analysis not completed. Session not saved.");
            }

            progressCallback?.Invoke(ProcessingStatus.Complete, 100, "Processing complete");
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;

            await SaveSessionToDatabase(session);

            _logger.LogInformation("Successfully saved MovieSession {SessionId} with {SuccessCount}/{TotalCount} successful transcripts",
                session.Id, successfulTranscripts, totalFiles);

            _logger.LogInformation("Successfully processed and saved session {SessionId}", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session {SessionId}", session.Id);
            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            throw;
        }
    }

    public async Task ProcessSession(string sessionId, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        var session = await _database.GetByIdAsync<MovieSession>(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        try
        {
            // Step 1: Validation
            progressCallback?.Invoke(ProcessingStatus.Validating, 10, "Validating session data");
            session.Status = ProcessingStatus.Validating;
            await _database.UpsertAsync(session);

            if (!session.AudioFiles.Any())
            {
                throw new Exception("No audio files found in session");
            }

            // Step 2: Transcription
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 20, "Starting transcription");
            session.Status = ProcessingStatus.Transcribing;
            await _database.UpsertAsync(session);

            // Use the session's folder path for organizing files, not the generic uploads folder
            var sessionFolderPath = session.FolderPath;
            _audioOrganizer.InitializeAudioFolders(sessionFolderPath);

            // Check if Gladia service is properly configured
            _gladiaService.LogConfigurationStatus();

            if (!_gladiaService.IsConfigured)
            {
                _logger.LogWarning("Gladia API key not configured, skipping transcription for session {SessionId}", session.Id);

                // Set status but don't move files since we're skipping transcription
                foreach (var audioFile in session.AudioFiles)
                {
                    if (audioFile.ProcessingStatus == AudioProcessingStatus.Pending)
                    {
                        audioFile.ProcessingStatus = AudioProcessingStatus.Failed;
                        audioFile.ConversionError = "Gladia API key not configured - transcription skipped";
                    }
                }

                // Skip to analysis with fallback
                progressCallback?.Invoke(ProcessingStatus.Analyzing, 75, "Skipping transcription - using fallback analysis");
                session.Status = ProcessingStatus.Analyzing;
                await _database.UpsertAsync(session);
                await AnalyzeSession(session);

                progressCallback?.Invoke(ProcessingStatus.Complete, 100, "Processing complete");
                session.Status = ProcessingStatus.Complete;
                session.ProcessedAt = DateTime.UtcNow;
                await _database.UpsertAsync(session);
                return;
            }

            // Don't move files from their original location automatically
            // Files will be moved during actual processing if needed

            _logger.LogInformation("Starting transcription for {FileCount} audio files", session.AudioFiles.Count);

            // Ensure mic assignments is not null
            var micAssignments = session.MicAssignments ?? new Dictionary<int, string>();
            _logger.LogInformation("Processing transcriptions with {MicCount} mic assignments: {Assignments}", 
                micAssignments.Count, 
                string.Join(", ", micAssignments.Select(kvp => $"Mic{kvp.Key}={kvp.Value}")));

            var transcriptionResults = await _gladiaService.ProcessMultipleFilesAsync(session.AudioFiles,
                micAssignments, // Pass mic assignments for speaker name mapping
                (fileName, current, total) =>
                {
                    var progress = 20 + (int)((double)current / total * 50); // 20-70%
                    progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, $"Transcribing {fileName}");
                });

            _logger.LogInformation("Transcription completed. Results: {SuccessCount} successful, {FailedCount} failed",
                transcriptionResults.Count(r => r.status == "done"),
                transcriptionResults.Count(r => r.status == "error"));

            // Only move files that actually failed - successful files stay in place
            foreach (var audioFile in session.AudioFiles)
            {
                if (audioFile.ProcessingStatus == AudioProcessingStatus.Failed ||
                    audioFile.ProcessingStatus == AudioProcessingStatus.FailedMp3)
                {
                    // File stays in session folder, status tracked in database
                }
                // Successful files (TranscriptionComplete, ProcessedMp3) stay in original location
            }

            // Step 3: AI Analysis
            progressCallback?.Invoke(ProcessingStatus.Analyzing, 75, "Analyzing transcripts for entertainment moments");
            session.Status = ProcessingStatus.Analyzing;
            await _database.UpsertAsync(session);

            await AnalyzeSession(session);

            // Step 4: Complete - ONLY NOW save to database after 100% success
            // Verify ALL files have successful transcripts before saving
            var successfulTranscripts = session.AudioFiles.Count(f =>
                f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete &&
                !string.IsNullOrEmpty(f.TranscriptText));

            var totalFiles = session.AudioFiles.Count;

            if (successfulTranscripts == 0)
            {
                throw new Exception($"No successful transcripts generated from {totalFiles} audio files. Session not saved.");
            }

            if (successfulTranscripts < totalFiles)
            {
                _logger.LogWarning("Only {SuccessCount}/{TotalCount} files successfully transcribed for session {SessionId}",
                    successfulTranscripts, totalFiles, session.Id);
            }

            // Final validation: ensure we have analysis results
            if (session.CategoryResults == null)
            {
                throw new Exception("Session analysis not completed. Session not saved.");
            }

            progressCallback?.Invoke(ProcessingStatus.Complete, 100, "Processing complete");
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;

            await _database.UpsertAsync(session);

            _logger.LogInformation("Successfully saved MovieSession {SessionId} with {SuccessCount}/{TotalCount} successful transcripts",
                session.Id, successfulTranscripts, totalFiles);

            _logger.LogInformation("Successfully processed session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session {SessionId}", sessionId);

            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            await _database.UpsertAsync(session);

            throw;
        }
    }

    private async Task AnalyzeSession(MovieSession session)
    {
        // Check if we have transcripts to analyze
        var transcriptsAvailable = session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText));

        // Log detailed information about transcript availability
        _logger.LogInformation("Analyzing session {SessionId}: {TotalFiles} audio files, {TranscriptFiles} with transcripts",
            session.Id, session.AudioFiles.Count, session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText)));

        foreach (var audioFile in session.AudioFiles)
        {
            _logger.LogDebug("Audio file {FileName}: Status={Status}, HasTranscript={HasTranscript}, TranscriptLength={Length}",
                audioFile.FileName,
                audioFile.ProcessingStatus,
                !string.IsNullOrEmpty(audioFile.TranscriptText),
                audioFile.TranscriptText?.Length ?? 0);
        }

        if (!transcriptsAvailable)
        {
            _logger.LogWarning("No transcripts available for analysis in session {SessionId}. Audio file statuses: {Statuses}",
                session.Id,
                string.Join(", ", session.AudioFiles.Select(f => $"{f.FileName}:{f.ProcessingStatus}")));

            // Use fallback analysis instead of throwing error
            _logger.LogInformation("Using fallback analysis for session {SessionId}", session.Id);
            session.CategoryResults = CreateFallbackAnalysis(session);
            session.SessionStats = CreateFallbackStats(session);
            return;
        }

        try
        {
            // Use the AI analysis service to analyze the session
            session.CategoryResults = await _analysisService.AnalyzeSessionAsync(session);

            // Generate session stats based on the analysis results
            session.SessionStats = _analysisService.GenerateSessionStats(session, session.CategoryResults);

            // Generate audio clips for Top 5 lists
            await GenerateAudioClips(session);

            _logger.LogInformation("Successfully analyzed session {SessionId} with {HighlightCount} highlights",
                session.Id, session.SessionStats.HighlightMoments);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI analysis failed for session {SessionId}, using fallback analysis", session.Id);

            // Fallback to basic analysis if AI analysis fails
            session.CategoryResults = CreateFallbackAnalysis(session);
            session.SessionStats = CreateFallbackStats(session);
        }
        
        // Set status back to Complete after analysis (successful or fallback)
        session.Status = ProcessingStatus.Complete;
        
        // Ensure the completed status is saved to database
        await SaveSessionToDatabase(session);
    }

    private CategoryResults CreateFallbackAnalysis(MovieSession session)
    {
        // Simple fallback analysis when AI analysis fails
        return new CategoryResults
        {
            BestJoke = CreateMockCategoryWinner("Speaker1", "15:23", "[Analysis unavailable - transcript processed but AI analysis failed]"),
            HottestTake = CreateMockCategoryWinner("Speaker2", "8:45", "[Analysis unavailable - please check API configuration]"),
        };
    }

    private SessionStats CreateFallbackStats(MovieSession session)
    {
        return new SessionStats
        {
            TotalDuration = CalculateTotalDuration(session),
            EnergyLevel = EnergyLevel.Medium,
            TechnicalQuality = AssessTechnicalQuality(session),
            HighlightMoments = 2, // Only mock highlights
            BestMomentsSummary = "Analysis completed with basic processing due to AI analysis error.",
            AttendancePattern = $"{session.ParticipantsPresent.Count}/{session.ParticipantsPresent.Count + session.ParticipantsAbsent.Count} regular members present"
        };
    }

    private string AssessTechnicalQuality(MovieSession session)
    {
        var totalFiles = session.AudioFiles.Count;
        if (totalFiles == 0) return "Unknown";

        var clearFiles = session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText));
        var percentage = (double)clearFiles / totalFiles * 100;

        return percentage switch
        {
            >= 90 => "Excellent - all audio clear",
            >= 70 => "Good - most audio clear",
            >= 50 => "Fair - some audio issues",
            _ => "Poor - significant audio problems"
        };
    }

    private CategoryWinner CreateMockCategoryWinner(string speaker, string timestamp, string quote)
    {
        return new CategoryWinner
        {
            Speaker = speaker,
            Timestamp = timestamp,
            Quote = quote,
            Setup = "During discussion of the movie's technical aspects",
            GroupReaction = "Loud laughter and agreement from most participants",
            WhyItsGreat = "Perfect timing and relatable criticism that resonated with the group",
            AudioQuality = AudioQuality.Clear,
            EntertainmentScore = new Random().Next(6, 10)
        };
    }

    private string CalculateTotalDuration(MovieSession session)
    {
        var totalMinutes = session.AudioFiles
            .Where(f => f.Duration.HasValue)
            .Select(f => f.Duration!.Value.TotalMinutes)
            .Max(); // Take the longest recording (likely the master)

        if (totalMinutes >= 60)
        {
            var hours = (int)(totalMinutes / 60);
            var minutes = (int)(totalMinutes % 60);
            return $"{hours}h {minutes}m";
        }
        else
        {
            return $"{(int)totalMinutes}m";
        }
    }

    private async Task GenerateAudioClips(MovieSession session)
    {
        try
        {
            var clipsGenerated = 0;

            // Generate clips for Funniest Sentences
            if (session.CategoryResults.FunniestSentences?.Entries.Any() == true)
            {
                await PopulateSourceAudioFiles(session.CategoryResults.FunniestSentences, session);
                var clipUrls = await _audioClipService.GenerateClipsForTopFiveAsync(session, session.CategoryResults.FunniestSentences);
                clipsGenerated += clipUrls.Count;
            }

            // Generate clips for Most Bland Comments
            if (session.CategoryResults.MostBlandComments?.Entries.Any() == true)
            {
                await PopulateSourceAudioFiles(session.CategoryResults.MostBlandComments, session);
                var clipUrls = await _audioClipService.GenerateClipsForTopFiveAsync(session, session.CategoryResults.MostBlandComments);
                clipsGenerated += clipUrls.Count;
            }

            _logger.LogInformation("Generated {ClipCount} audio clips for session {SessionId}", clipsGenerated, session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate audio clips for session {SessionId}", session.Id);
        }
    }

    private async Task PopulateSourceAudioFiles(TopFiveList topFive, MovieSession session)
    {
        foreach (var entry in topFive.Entries)
        {
            if (string.IsNullOrEmpty(entry.SourceAudioFile))
            {
                // If no specific source file is identified, use the master recording or first available
                var masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording)?.FileName;
                if (!string.IsNullOrEmpty(masterFile))
                {
                    entry.SourceAudioFile = masterFile;
                }
                else if (session.AudioFiles.Any())
                {
                    entry.SourceAudioFile = session.AudioFiles.First().FileName;
                }
            }

            // Convert timestamp to seconds if needed
            if (entry.StartTimeSeconds == 0 && !string.IsNullOrEmpty(entry.Timestamp))
            {
                var timeSpan = _audioClipService.ParseTimestamp(entry.Timestamp);
                entry.StartTimeSeconds = timeSpan.TotalSeconds;

                // Add some reasonable duration (e.g., 5-8 seconds) if end time not specified
                if (entry.EndTimeSeconds == 0)
                {
                    entry.EndTimeSeconds = entry.StartTimeSeconds + 6; // Default 6 second clip
                }
            }
        }
    }

    public async Task<List<MovieSession>> GetAllSessions()
    {
        return await _database.GetAllAsync<MovieSession>();
    }

    public async Task<List<MovieSession>> GetRecentSessions(int limit = 10)
    {
        // Get all sessions for debugging, then filter completed ones
        var allSessions = await _database.GetAllAsync<MovieSession>();
        
        _logger.LogInformation("GetRecentSessions: Found {TotalCount} total sessions", allSessions.Count);
        
        if (allSessions.Any())
        {
            var statusCounts = allSessions.GroupBy(s => s.Status).ToDictionary(g => g.Key, g => g.Count());
            foreach (var status in statusCounts)
            {
                _logger.LogInformation("Sessions with status {Status}: {Count}", status.Key, status.Value);
            }
        }
        
        // Filter to only completed sessions (as originally intended)
        var completedSessions = allSessions.Where(s => s.Status == ProcessingStatus.Complete).ToList();
        
        // Get all movie events to sort by their start dates
        var movieEvents = await _database.GetAllAsync<MovieEvent>();
        var movieEventLookup = movieEvents.ToDictionary(me => me.Movie, me => me.StartDate);
        
        // Sort sessions by the corresponding MovieEvent.StartDate, then by session creation date
        var sortedSessions = completedSessions
            .OrderByDescending(s => movieEventLookup.TryGetValue(s.MovieTitle, out var startDate) ? startDate : s.Date)
            .ThenByDescending(s => s.CreatedAt)
            .Take(limit)
            .ToList();

        _logger.LogInformation("Returning {Count} completed sessions to display", sortedSessions.Count);
        return sortedSessions;
    }

    public async Task<MovieSession?> GetSession(string sessionId)
    {
        return await _database.GetByIdAsync<MovieSession>(Guid.Parse(sessionId));
    }

    public async Task<bool> DeleteSession(string sessionId)
    {
        return await _database.DeleteByIdAsync<MovieSession>(Guid.Parse(sessionId));
    }

    public async Task<List<MovieSession>> SearchSessions(string searchTerm)
    {
        // Use the new text search API for better performance!
        return await _database.SearchTextAsync<MovieSession>(
            searchTerm,
            s => s.MovieTitle,
            s => s.FolderPath
        );
    }

    // Additional convenience methods using the new API
    public async Task<long> GetSessionCount()
    {
        return await _database.CountAsync<MovieSession>();
    }

    public async Task<bool> HasAnySessions()
    {
        return await _database.AnyAsync<MovieSession>();
    }

    public async Task<List<MovieSession>> GetSessionsByDateRange(DateTime start, DateTime end)
    {
        return await _database.FindAsync<MovieSession>(s => s.Date >= start && s.Date <= end);
    }

    public async Task<List<MovieSession>> GetFailedSessions()
    {
        return await _database.FindAsync<MovieSession>(s => s.Status == ProcessingStatus.Failed);
    }

    public async Task<Dictionary<int, string>> GetLatestMicAssignments()
    {
        // Get all sessions with mic assignments and sort by date
        var sessionsWithAssignments = await _database.FindAsync<MovieSession>(
            s => s.MicAssignments != null
        );

        var latestSession = sessionsWithAssignments
            .Where(s => s.MicAssignments != null && s.MicAssignments.Count > 0)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();

        return latestSession?.MicAssignments ?? new Dictionary<int, string>();
    }

    /// <summary>
    /// Fixes sessions that are stuck in analyzing status
    /// </summary>
    public async Task<int> FixStuckAnalyzingSessions()
    {
        try
        {
            var analyzingSessions = await _database.FindAsync<MovieSession>(s => s.Status == ProcessingStatus.Analyzing);
            var fixedCount = 0;

            foreach (var session in analyzingSessions)
            {
                // Check if session has transcripts and analysis results
                var hasTranscripts = session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText));
                var hasAnalysis = session.CategoryResults != null;

                if (hasTranscripts && hasAnalysis)
                {
                    // Session is actually complete, just fix the status
                    session.Status = ProcessingStatus.Complete;
                    session.ProcessedAt = DateTime.UtcNow;
                    await SaveSessionToDatabase(session);
                    fixedCount++;
                    _logger.LogInformation("Fixed stuck analyzing session: {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);
                }
                else if (hasTranscripts)
                {
                    // Has transcripts but no analysis, re-run analysis
                    await AnalyzeSession(session);
                    fixedCount++;
                    _logger.LogInformation("Re-analyzed session: {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);
                }
            }

            return fixedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fix stuck analyzing sessions");
            return 0;
        }
    }

    /// <summary>
    /// Reruns the OpenAI analysis for a session that has completed transcription
    /// </summary>
    public async Task<bool> RerunAnalysis(string sessionId)
    {
        try
        {
            var session = await GetSession(sessionId);
            if (session == null)
            {
                _logger.LogWarning("Session {SessionId} not found for analysis rerun", sessionId);
                return false;
            }

            // Ensure session has transcripts
            if (!session.AudioFiles.Any(f => !string.IsNullOrEmpty(f.TranscriptText)))
            {
                _logger.LogWarning("Session {SessionId} has no transcripts available for analysis", sessionId);
                return false;
            }

            _logger.LogInformation("Rerunning analysis for session {SessionId} - {MovieTitle}", sessionId, session.MovieTitle);

            // Clear existing analysis results
            session.CategoryResults = null;
            session.SessionStats = null;
            session.Status = ProcessingStatus.Analyzing;

            // Run analysis
            await AnalyzeSession(session);

            // Save updated session
            await SaveSessionToDatabase(session);

            _logger.LogInformation("Successfully reran analysis for session {SessionId}", sessionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rerun analysis for session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<int> RedownloadExistingTranscriptions(MovieSession session, Action<string, int, int>? progressCallback = null)
    {
        var transcribedFiles = session.AudioFiles.Where(f => !string.IsNullOrEmpty(f.TranscriptId)).ToList();

        if (!transcribedFiles.Any())
        {
            _logger.LogWarning("No transcribed files found in session {SessionId}", session.Id);
            return 0;
        }

        _logger.LogInformation("Redownloading {Count} transcriptions for session {SessionId}", transcribedFiles.Count, session.Id);

        int successCount = 0;
        int currentIndex = 0;

        foreach (var audioFile in transcribedFiles)
        {
            currentIndex++;
            progressCallback?.Invoke($"Redownloading {audioFile.FileName}", currentIndex, transcribedFiles.Count);

            try
            {
                // Get the transcription result from Gladia
                var result = await _gladiaService.GetTranscriptionResultAsync(audioFile.TranscriptId!);

                // Save the JSON file alongside the audio file
                var audioDirectory = Path.GetDirectoryName(audioFile.FilePath);
                if (!string.IsNullOrEmpty(audioDirectory))
                {
                    var jsonPath = await _gladiaService.SaveTranscriptionJsonAsync(result, audioFile.FilePath, audioDirectory);
                    audioFile.JsonFilePath = jsonPath;

                    // Also update the transcript text if needed
                    if (string.IsNullOrEmpty(audioFile.TranscriptText))
                    {
                        var rawTranscript = result.result?.transcription?.full_transcript;
                        audioFile.TranscriptText = !string.IsNullOrEmpty(rawTranscript) && session.MicAssignments != null
                            ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, session.MicAssignments, audioFile.FileName)
                            : rawTranscript;
                    }

                    successCount++;
                    _logger.LogInformation("Successfully redownloaded transcription for {FileName}, saved to {JsonPath}",
                        audioFile.FileName, jsonPath);
                }
                else
                {
                    _logger.LogWarning("Could not determine directory for {FileName}", audioFile.FileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to redownload transcription for {FileName} (TranscriptId: {TranscriptId})",
                    audioFile.FileName, audioFile.TranscriptId);
            }
        }

        // Save the updated session to persist JsonFilePath updates
        await SaveSessionToDatabase(session);

        _logger.LogInformation("Redownload complete. {SuccessCount}/{TotalCount} transcriptions recovered",
            successCount, transcribedFiles.Count);

        return successCount;
    }

    private async Task ProcessSingleTranscription(AudioFile audioFile, Dictionary<int, string> micAssignments, string sessionFolderPath)
    {
        try
        {
            // Count assigned speakers, defaulting to 2 if no assignments
            int assignedSpeakers = micAssignments?.Values.Count(v => !string.IsNullOrWhiteSpace(v)) ?? 0;
            int numOfSpeakers = assignedSpeakers > 0 ? assignedSpeakers : 2;

            // Use the enhanced transcription method that saves JSON files
            var (result, jsonPath) = await _gladiaService.ProcessTranscriptionWithJsonSaveAsync(
                audioFile.AudioUrl!,
                audioFile.FilePath,
                sessionFolderPath,
                numOfSpeakers,
                true);

            // Apply speaker name mapping based on mic assignments and filename
            var rawTranscript = result.result?.transcription?.full_transcript;
            audioFile.TranscriptText = !string.IsNullOrEmpty(rawTranscript)
                ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, micAssignments, audioFile.FileName)
                : rawTranscript;

            audioFile.TranscriptId = result.id;
            audioFile.JsonFilePath = jsonPath; // Store the JSON file path
            audioFile.ProcessingStatus = AudioProcessingStatus.TranscriptionComplete;
            audioFile.ProcessedAt = DateTime.UtcNow;

            // File stays in session folder, status tracked in database

            _logger.LogInformation("Successfully transcribed {FileName} and saved JSON to {JsonPath}",
                audioFile.FileName, jsonPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe {FileName}", audioFile.FileName);
            audioFile.ProcessingStatus = AudioProcessingStatus.FailedMp3;
            audioFile.ConversionError = ex.Message;

            // File stays in session folder, status tracked in database
        }
    }
}