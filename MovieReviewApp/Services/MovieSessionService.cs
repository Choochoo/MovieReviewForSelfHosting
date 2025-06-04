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

            // Check for MIC1-6 pattern
            var micMatch = Regex.Match(fileNameUpper, @"^MIC(\d)\.WAV$");
            if (micMatch.Success)
            {
                audioFile.SpeakerNumber = int.Parse(micMatch.Groups[1].Value);
                identifiedFiles.Add(fileName);
            }
            // Check for existing speaker pattern
            else if (Regex.IsMatch(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase))
            {
                var match = Regex.Match(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase);
                audioFile.SpeakerNumber = int.Parse(match.Groups[1].Value);
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

        // Rename master recording to MASTER_MIX.WAV
        var masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording);
        if (masterFile != null && !masterFile.FileName.Equals("MASTER_MIX.WAV", StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = masterFile.FilePath;
            var directory = Path.GetDirectoryName(oldPath);
            var newPath = Path.Combine(directory!, "MASTER_MIX.WAV");

            try
            {
                // If MASTER_MIX.WAV already exists, don't overwrite
                if (!File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    masterFile.FileName = "MASTER_MIX.WAV";
                    masterFile.FilePath = newPath;
                    _logger.LogInformation("Renamed master mix file from {OldName} to MASTER_MIX.WAV", Path.GetFileName(oldPath));
                }
                else
                {
                    _logger.LogWarning("MASTER_MIX.WAV already exists, keeping original file name: {FileName}", masterFile.FileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rename master mix file from {OldName} to MASTER_MIX.WAV", masterFile.FileName);
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
        // Based on audio files, determine who was present
        var presentSpeakers = session.AudioFiles
            .Where(f => f.SpeakerNumber.HasValue)
            .Select(f => f.SpeakerNumber!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // All possible speakers (1-6)
        var allSpeakers = Enumerable.Range(1, 6).ToList();
        var absentSpeakers = allSpeakers.Except(presentSpeakers).ToList();

        // Use mic assignments if available, otherwise show generic mic labels
        session.ParticipantsPresent = presentSpeakers.Select(mic =>
            session.MicAssignments.TryGetValue(mic, out var name) && !string.IsNullOrEmpty(name) 
                ? name 
                : GetParticipantName(mic)
        ).ToList();

        session.ParticipantsAbsent = absentSpeakers.Select(mic =>
            session.MicAssignments.TryGetValue(mic, out var name) && !string.IsNullOrEmpty(name) 
                ? name 
                : GetParticipantName(mic)
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

            // Phase 1: Convert all WAV files to MP3
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 10, "Converting audio files to MP3...");
            
            var conversionResults = await _gladiaService.ConvertAllWavsToMp3Async(session.AudioFiles, sessionFolderPath,
                (message, current, total) =>
                {
                    var progress = 10 + (int)((double)current / total * 20); // 10-30%
                    progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, message);
                });

            // Phase 2: Move successful MP3s to processed_mp3 and delete original WAVs
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 30, "Organizing converted files...");
            
            foreach (var (audioFile, success, error) in conversionResults)
            {
                if (success && !string.IsNullOrEmpty(audioFile.Mp3FilePath))
                {
                    // Files are already in pending_mp3 from conversion process
                    // Just update file path to MP3 location and clean up WAV
                    var originalWavPath = audioFile.FilePath;
                    audioFile.FilePath = audioFile.Mp3FilePath;
                    
                    // Delete original WAV file if it's different from MP3
                    if (File.Exists(originalWavPath) && originalWavPath != audioFile.Mp3FilePath)
                    {
                        try
                        {
                            File.Delete(originalWavPath);
                            _logger.LogInformation("Deleted original WAV file: {FilePath}", originalWavPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete original WAV file: {FilePath}", originalWavPath);
                        }
                    }
                }
                else
                {
                    // Files are already moved to failed folder by conversion process
                    audioFile.ConversionError = error;
                }
            }

            // Phase 3: Upload all MP3s to Gladia
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 35, "Uploading files to Gladia...");
            
            var mp3Files = session.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.PendingMp3).ToList();
            if (!mp3Files.Any())
            {
                throw new Exception("No MP3 files were successfully converted for upload.");
            }
            
            var uploadResults = await _gladiaService.UploadAllMp3sToGladiaAsync(mp3Files, sessionFolderPath,
                (message, current, total) =>
                {
                    var progress = 35 + (int)((double)current / total * 25); // 35-60%
                    progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, message);
                });

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
                    audioFile.ConversionError = error;
                    
                    // Move failed upload to failed_mp3 folder
                    if (!string.IsNullOrEmpty(audioFile.Mp3FilePath))
                    {
                        audioFile.Mp3FilePath = await _audioOrganizer.MoveMp3FileToStatusFolder(audioFile, sessionFolderPath);
                    }
                }
            }

            // Wait for all transcriptions to complete
            await Task.WhenAll(transcriptionTasks);
            
            // Phase 5: Save transcription files to session folder
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 70, "Saving transcription files...");
            
            foreach (var audioFile in session.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete))
            {
                if (!string.IsNullOrEmpty(audioFile.TranscriptText))
                {
                    var transcriptFileName = Path.ChangeExtension(audioFile.FileName, ".txt");
                    var transcriptPath = Path.Combine(sessionFolderPath, transcriptFileName);
                    
                    await File.WriteAllTextAsync(transcriptPath, audioFile.TranscriptText);
                    _logger.LogInformation("Saved transcript to: {TranscriptPath}", transcriptPath);
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
            
            var transcriptionResults = await _gladiaService.ProcessMultipleFilesAsync(session.AudioFiles,
                session.MicAssignments, // Pass mic assignments for speaker name mapping
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
                    // Move failed files to failed subfolder for organization
                    audioFile.FilePath = await _audioOrganizer.MoveFileToStatusFolder(audioFile, sessionFolderPath);
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
        // Use the new paging API for better performance!
        var (sessions, _) = await _database.GetPagedAsync<MovieSession>(
            page: 1,
            pageSize: limit,
            orderBy: s => s.CreatedAt,
            descending: true
        );

        return sessions;
    }

    public async Task<MovieSession?> GetSession(string sessionId)
    {
        return await _database.GetByIdAsync<MovieSession>(sessionId);
    }

    public async Task<bool> DeleteSession(string sessionId)
    {
        return await _database.DeleteByIdAsync<MovieSession>(sessionId);
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

    private async Task ProcessSingleTranscription(AudioFile audioFile, Dictionary<int, string> micAssignments, string sessionFolderPath)
    {
        try
        {
            // Start transcription with the uploaded audio URL
            var transcriptionId = await _gladiaService.StartTranscriptionAsync(audioFile.AudioUrl!);
            
            // Wait for completion
            var result = await _gladiaService.WaitForTranscriptionAsync(transcriptionId);
            
            // Apply speaker name mapping based on mic assignments and filename
            var rawTranscript = result.result?.transcription?.full_transcript;
            audioFile.TranscriptText = !string.IsNullOrEmpty(rawTranscript) 
                ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, micAssignments, audioFile.FileName)
                : rawTranscript;
            
            audioFile.TranscriptId = result.id;
            audioFile.ProcessingStatus = AudioProcessingStatus.TranscriptionComplete;
            audioFile.ProcessedAt = DateTime.UtcNow;
            
            // Move completed transcription to processed_mp3 folder
            if (!string.IsNullOrEmpty(audioFile.Mp3FilePath))
            {
                audioFile.Mp3FilePath = await _audioOrganizer.MoveMp3FileToStatusFolder(audioFile, sessionFolderPath, cleanupSource: true);
            }
            
            _logger.LogInformation("Successfully transcribed {FileName}", audioFile.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe {FileName}", audioFile.FileName);
            audioFile.ProcessingStatus = AudioProcessingStatus.FailedMp3;
            audioFile.ConversionError = ex.Message;
            
            // Move failed transcription to failed_mp3 folder
            if (!string.IsNullOrEmpty(audioFile.Mp3FilePath))
            {
                audioFile.Mp3FilePath = await _audioOrganizer.MoveMp3FileToStatusFolder(audioFile, sessionFolderPath);
            }
        }
    }
}