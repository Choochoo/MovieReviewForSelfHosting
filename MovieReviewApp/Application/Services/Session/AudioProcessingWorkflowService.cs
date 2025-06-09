using MovieReviewApp.Models;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Infrastructure.FileSystem;

namespace MovieReviewApp.Application.Services.Session;

/// <summary>
/// Service responsible for orchestrating the audio processing workflow including conversion, upload, and transcription.
/// Handles the complete pipeline from raw audio files to processed transcripts.
/// </summary>
public class AudioProcessingWorkflowService
{
    private readonly GladiaService _gladiaService;
    private readonly AudioFileOrganizer _audioOrganizer;
    private readonly ILogger<AudioProcessingWorkflowService> _logger;

    public AudioProcessingWorkflowService(
        GladiaService gladiaService,
        AudioFileOrganizer audioOrganizer,
        ILogger<AudioProcessingWorkflowService> logger)
    {
        _gladiaService = gladiaService;
        _audioOrganizer = audioOrganizer;
        _logger = logger;
    }

    /// <summary>
    /// Processes a movie session through the enhanced workflow including all audio processing steps.
    /// </summary>
    public async Task<MovieSession> ProcessSessionEnhancedAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Starting enhanced processing workflow", 0);
        
        try
        {
            session.Status = ProcessingStatus.Validating;
            progressCallback?.Invoke("Session validated and saved", 10);

            // Phase 1: Convert WAV files to MP3
            await ConvertAudioFilesAsync(session, progressCallback);
            
            // Phase 2: Upload to Gladia
            await UploadAudioFilesAsync(session, progressCallback);
            
            // Phase 3: Process transcriptions
            await ProcessTranscriptionsAsync(session, progressCallback);
            
            progressCallback?.Invoke("Audio processing complete", 75);
            return session;
        }
        catch (Exception ex)
        {
            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// Converts WAV audio files to MP3 format for processing.
    /// </summary>
    public async Task ConvertAudioFilesAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        List<AudioFile> wavFiles = session.AudioFiles.Where(f => f.FilePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!wavFiles.Any()) return;

        progressCallback?.Invoke("Converting WAV files to MP3", 20);
        
        for (int i = 0; i < wavFiles.Count; i++)
        {
            AudioFile file = wavFiles[i];
            file.ProcessingStatus = AudioProcessingStatus.ConvertingToMp3;
            file.CurrentStep = "Converting to MP3";
            file.ProgressPercentage = 0;

            try
            {
                List<(AudioFile audioFile, bool success, string? error)> results = await _gladiaService.ConvertAllWavsToMp3Async(
                    new List<AudioFile> { file }, 
                    session.FolderPath,
                    (step, current, total) => 
                    {
                        file.ProgressPercentage = (int)((double)current / total * 100);
                        file.CurrentStep = step;
                    });

                (AudioFile audioFile, bool success, string? error) result = results.FirstOrDefault();
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

            progressCallback?.Invoke($"Converted {i + 1}/{wavFiles.Count} files", 20 + (i + 1) * 20 / wavFiles.Count);
        }
    }

    /// <summary>
    /// Uploads MP3 audio files to the Gladia service for transcription processing.
    /// </summary>
    public async Task UploadAudioFilesAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        List<AudioFile> filesToUpload = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.PendingMp3 && 
            string.IsNullOrEmpty(f.AudioUrl)).ToList();
        
        if (!filesToUpload.Any()) return;

        progressCallback?.Invoke("Uploading files to Gladia", 40);

        for (int i = 0; i < filesToUpload.Count; i++)
        {
            AudioFile file = filesToUpload[i];
            file.ProcessingStatus = AudioProcessingStatus.UploadingToGladia;
            file.CurrentStep = "Uploading to Gladia";
            file.ProgressPercentage = 0;

            try
            {
                List<(AudioFile audioFile, bool success, string? error)> results = await _gladiaService.UploadAllMp3sToGladiaAsync(
                    new List<AudioFile> { file },
                    session.FolderPath,
                    (step, current, total) => 
                    {
                        file.ProgressPercentage = (int)((double)current / total * 100);
                        file.CurrentStep = step;
                    },
                    session,
                    null); // No save callback for this service

                (AudioFile audioFile, bool success, string? error) result = results.FirstOrDefault();
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

            progressCallback?.Invoke($"Uploaded {i + 1}/{filesToUpload.Count} files", 40 + (i + 1) * 20 / filesToUpload.Count);
        }
    }

    /// <summary>
    /// Processes transcriptions for uploaded audio files using the Gladia service.
    /// </summary>
    public async Task ProcessTranscriptionsAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        List<AudioFile> filesToTranscribe = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.UploadedToGladia && 
            !string.IsNullOrEmpty(f.AudioUrl) &&
            string.IsNullOrEmpty(f.TranscriptText)).ToList();

        if (!filesToTranscribe.Any()) return;

        progressCallback?.Invoke("Processing transcriptions", 60);

        for (int i = 0; i < filesToTranscribe.Count; i++)
        {
            AudioFile file = filesToTranscribe[i];
            file.ProcessingStatus = AudioProcessingStatus.Transcribing;
            file.CurrentStep = "Starting transcription";
            file.ProgressPercentage = 0;

            try
            {
                // Start transcription
                int numSpeakers = session.MicAssignments?.Count ?? 2;
                string transcriptionId = await _gladiaService.StartTranscriptionAsync(
                    file.AudioUrl!, numSpeakers, true, file.FileName);
                
                file.TranscriptId = transcriptionId;
                file.CurrentStep = "Transcription in progress";
                file.ProgressPercentage = 50;

                // Wait for completion
                dynamic result = await _gladiaService.WaitForTranscriptionAsync(transcriptionId);
                
                // Apply speaker mapping
                string? rawTranscript = result.result?.transcription?.full_transcript;
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

            progressCallback?.Invoke($"Transcribed {i + 1}/{filesToTranscribe.Count} files", 60 + (i + 1) * 20 / filesToTranscribe.Count);
        }
    }

    /// <summary>
    /// Processes standard session workflow with detailed file organization and error handling.
    /// </summary>
    public async Task ProcessSessionStandardAsync(MovieSession session, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        progressCallback?.Invoke(ProcessingStatus.Transcribing, 20, "Starting transcription");

        // Use the session's folder path for organizing files
        string sessionFolderPath = session.FolderPath;
        _audioOrganizer.InitializeAudioFolders(sessionFolderPath);

        // Check if Gladia service is properly configured
        _gladiaService.LogConfigurationStatus();

        if (!_gladiaService.IsConfigured)
        {
            throw new Exception("Gladia API key not configured. Cannot process audio files.");
        }

        // Phase 1: Convert WAV files to MP3
        await ConvertWavFilesAsync(session, sessionFolderPath, progressCallback);

        // Phase 2: Upload MP3s to Gladia
        await UploadMp3FilesAsync(session, sessionFolderPath, progressCallback);

        // Phase 3: Process transcriptions
        await ProcessTranscriptionBatchAsync(session, progressCallback);

        // Phase 4: Save transcript files
        await SaveTranscriptFilesAsync(session, progressCallback);
    }

    /// <summary>
    /// Converts WAV files to MP3 format for the standard processing workflow.
    /// </summary>
    private async Task ConvertWavFilesAsync(MovieSession session, string sessionFolderPath, Action<ProcessingStatus, int, string>? progressCallback)
    {
        List<AudioFile> filesToConvert = session.AudioFiles.Where(f =>
            (f.ProcessingStatus == AudioProcessingStatus.Pending ||
             f.ProcessingStatus == AudioProcessingStatus.Failed ||
             (Path.GetExtension(f.FilePath).ToLowerInvariant() == ".wav" &&
              f.ProcessingStatus == AudioProcessingStatus.PendingMp3))).ToList();

        if (filesToConvert.Any())
        {
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 10, "Converting audio files to MP3...");

            List<(AudioFile audioFile, bool success, string? error)> conversionResults = await _gladiaService.ConvertAllWavsToMp3Async(filesToConvert, sessionFolderPath,
                (message, current, total) =>
                {
                    int progress = 10 + (int)((double)current / total * 20); // 10-30%
                    progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, message);
                });

            progressCallback?.Invoke(ProcessingStatus.Transcribing, 30, "Organizing converted files...");

            foreach ((AudioFile audioFile, bool success, string? error) in conversionResults)
            {
                if (success)
                {
                    _logger.LogInformation("File {FileName} successfully converted to MP3", audioFile.FileName);
                }
                else
                {
                    audioFile.ConversionError = error ?? "Unknown error during conversion";
                }
            }
        }
        else
        {
            _logger.LogInformation("Skipping MP3 conversion - no files need conversion");
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 30, "No files need MP3 conversion");
        }
    }

    /// <summary>
    /// Uploads MP3 files to Gladia service for transcription.
    /// </summary>
    private async Task UploadMp3FilesAsync(MovieSession session, string sessionFolderPath, Action<ProcessingStatus, int, string>? progressCallback)
    {
        progressCallback?.Invoke(ProcessingStatus.Transcribing, 35, "Uploading files to Gladia...");

        List<AudioFile> mp3Files = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.PendingMp3 || 
            f.ProcessingStatus == AudioProcessingStatus.FailedMp3 || 
            f.ProcessingStatus == AudioProcessingStatus.ProcessedMp3).ToList();
            
        if (!mp3Files.Any())
        {
            throw new Exception("No MP3 files were successfully converted for upload.");
        }

        List<(AudioFile audioFile, bool success, string? error)> uploadResults = await _gladiaService.UploadAllMp3sToGladiaAsync(mp3Files, sessionFolderPath,
            (message, current, total) =>
            {
                int progress = 35 + (int)((double)current / total * 25); // 35-60%
                progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, message);
            },
            session,
            null); // No save callback for this service
    }

    /// <summary>
    /// Processes transcription tasks for uploaded audio files.
    /// </summary>
    private async Task ProcessTranscriptionBatchAsync(MovieSession session, Action<ProcessingStatus, int, string>? progressCallback)
    {
        progressCallback?.Invoke(ProcessingStatus.Transcribing, 60, "Starting transcriptions...");

        List<Task> transcriptionTasks = new List<Task>();
        List<AudioFile> successfulUploads = session.AudioFiles.Where(f => 
            f.ProcessingStatus == AudioProcessingStatus.UploadedToGladia && 
            !string.IsNullOrEmpty(f.AudioUrl)).ToList();

        foreach (AudioFile audioFile in successfulUploads)
        {
            transcriptionTasks.Add(ProcessSingleTranscriptionAsync(audioFile, session.MicAssignments, session.FolderPath));
        }

        // Wait for all transcriptions to complete
        await Task.WhenAll(transcriptionTasks);
    }

    /// <summary>
    /// Saves transcript files to the filesystem alongside audio files.
    /// </summary>
    private async Task SaveTranscriptFilesAsync(MovieSession session, Action<ProcessingStatus, int, string>? progressCallback)
    {
        progressCallback?.Invoke(ProcessingStatus.Transcribing, 70, "Saving transcription files...");

        foreach (AudioFile audioFile in session.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.TranscriptionComplete))
        {
            if (!string.IsNullOrEmpty(audioFile.TranscriptText))
            {
                try
                {
                    string? audioDirectory = Path.GetDirectoryName(audioFile.FilePath);
                    if (!string.IsNullOrEmpty(audioDirectory) && Directory.Exists(audioDirectory))
                    {
                        string transcriptFileName = Path.ChangeExtension(audioFile.FileName, ".txt");
                        string transcriptPath = Path.Combine(audioDirectory, transcriptFileName);

                        await File.WriteAllTextAsync(transcriptPath, audioFile.TranscriptText);
                        _logger.LogInformation("Saved transcript to: {TranscriptPath}", transcriptPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save transcript for {FileName}", audioFile.FileName);
                }
            }
        }
    }

    /// <summary>
    /// Processes a single audio file transcription with JSON file saving.
    /// </summary>
    private async Task ProcessSingleTranscriptionAsync(AudioFile audioFile, Dictionary<int, string>? micAssignments, string sessionFolderPath)
    {
        try
        {
            int assignedSpeakers = micAssignments?.Values.Count(v => !string.IsNullOrWhiteSpace(v)) ?? 0;
            int numOfSpeakers = assignedSpeakers > 0 ? assignedSpeakers : 2;

            (dynamic result, string jsonPath) = await _gladiaService.ProcessTranscriptionWithJsonSaveAsync(
                audioFile.AudioUrl!,
                audioFile.FilePath,
                sessionFolderPath,
                numOfSpeakers,
                true);

            string? rawTranscript = result.result?.transcription?.full_transcript;
            audioFile.TranscriptText = !string.IsNullOrEmpty(rawTranscript)
                ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, micAssignments, audioFile.FileName)
                : rawTranscript;

            audioFile.TranscriptId = result.id;
            audioFile.JsonFilePath = jsonPath;
            audioFile.ProcessingStatus = AudioProcessingStatus.TranscriptionComplete;
            audioFile.ProcessedAt = DateTime.UtcNow;

            _logger.LogInformation("Successfully transcribed {FileName} and saved JSON to {JsonPath}",
                audioFile.FileName, jsonPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe {FileName}", audioFile.FileName);
            audioFile.ProcessingStatus = AudioProcessingStatus.FailedMp3;
            audioFile.ConversionError = ex.Message;
        }
    }
}