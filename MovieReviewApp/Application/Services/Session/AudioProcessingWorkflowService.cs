using System.Text;
using System.Text.Json;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Application.Services.Analysis;
using Microsoft.Extensions.Logging;

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
    /// Files are processed individually through each stage for better parallel processing.
    /// </summary>
    public async Task<MovieSession> ProcessSessionEnhancedAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Starting enhanced processing workflow", 0);

        try
        {
            session.Status = ProcessingStatus.Validating;
            progressCallback?.Invoke("Session validated and saved", 10);

            // Process each file individually through all stages (conversion, upload, transcription)
            await ProcessFilesIndividuallyAsync(session, progressCallback);

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
    /// Processes files completely in parallel through all individual stages, then synchronizes for combined processing.
    /// Each file goes through: WAV→MP3 conversion → upload to Gladia → transcription → JSON download.
    /// Only when ALL files complete their individual processing does the system proceed to synchronous transcription processing.
    /// </summary>
    private async Task ProcessFilesIndividuallyAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        List<AudioFile> allFiles = session.AudioFiles.ToList();
        int totalFiles = allFiles.Count;

        // Create parallel tasks for each file to process through all individual stages
        List<Task> fileProcessingTasks = new List<Task>();

        for (int i = 0; i < allFiles.Count; i++)
        {
            AudioFile file = allFiles[i];
            // Create a task for each file that includes periodic UI updates
            Task fileTask = ProcessSingleFileWithUIUpdatesAsync(file, session, progressCallback);
            fileProcessingTasks.Add(fileTask);
        }

        progressCallback?.Invoke($"Processing {totalFiles} files in parallel...", 10);

        // Wait for ALL files to complete their individual processing (conversion → upload → transcription → JSON download)
        await Task.WhenAll(fileProcessingTasks);

        progressCallback?.Invoke("All files completed individual processing", 80);

        // Now wait for ALL transcriptions to be completely finished before proceeding
        await WaitForAllTranscriptionsToComplete(session, progressCallback);

        progressCallback?.Invoke("All transcriptions complete - ready for combined processing", 90);
    }

    /// <summary>
    /// Continuously checks if all files have completed transcription and waits until they do.
    /// This replaces the ValidateAllFilesTranscribedAndProceed method to actually wait instead of throwing exceptions.
    /// </summary>
    private async Task WaitForAllTranscriptionsToComplete(MovieSession session, Action<string, int>? progressCallback = null)
    {
        List<AudioFile> allFiles = session.AudioFiles.ToList();
        _logger.LogInformation("Waiting for all {FileCount} files to complete transcription for session {SessionId}",
            allFiles.Count, session.Id);

        while (true)
        {
            List<AudioFile> transcribedFiles = allFiles.Where(f =>
                f.ProcessingStatus == AudioProcessingStatus.TranscriptsDownloaded &&
                !string.IsNullOrEmpty(f.TranscriptText)).ToList();

            List<AudioFile> pendingFiles = allFiles.Where(f =>
                f.ProcessingStatus != AudioProcessingStatus.TranscriptsDownloaded ||
                string.IsNullOrEmpty(f.TranscriptText)).ToList();

            List<AudioFile> failedFiles = allFiles.Where(f =>
                f.ProcessingStatus == AudioProcessingStatus.Failed).ToList();

            _logger.LogDebug("Transcription status: {TranscribedCount}/{TotalCount} completed, {PendingCount} pending, {FailedCount} failed",
                transcribedFiles.Count, allFiles.Count, pendingFiles.Count, failedFiles.Count);

            if (transcribedFiles.Count == allFiles.Count)
            {
                // All files completed successfully
                _logger.LogInformation("All {FileCount} files have completed transcription successfully", allFiles.Count);
                break;
            }

            if (failedFiles.Count > 0 && (transcribedFiles.Count + failedFiles.Count) == allFiles.Count)
            {
                // Some files failed but no more are pending - we can't proceed
                string failedFileNames = string.Join(", ", failedFiles.Select(f => f.FileName));
                string errorMessage = $"Cannot proceed: {failedFiles.Count} files failed transcription: {failedFileNames}";
                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            // Update status for transcribed files
            foreach (AudioFile file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.WaitingForOtherFiles;
                file.CurrentStep = $"Waiting for {pendingFiles.Count} other files to complete transcription";
                file.LastUpdated = DateTime.UtcNow;
            }

            // Report progress
            string pendingFileNames = string.Join(", ", pendingFiles.Take(3).Select(f => f.FileName));
            if (pendingFiles.Count > 3) pendingFileNames += $" and {pendingFiles.Count - 3} more";

            string message = $"Waiting for {pendingFiles.Count} files to complete transcription: {pendingFileNames}";
            progressCallback?.Invoke(message, 80 + (transcribedFiles.Count * 10 / allFiles.Count));

            // Wait before checking again
            await Task.Delay(2000); // Check every 2 seconds
        }

        // Mark all files as ready for combined processing
        foreach (AudioFile file in allFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.WaitingForOtherFiles))
        {
            file.ProcessingStatus = AudioProcessingStatus.ProcessingTranscriptions;
            file.CurrentStep = "Processing all transcriptions together";
            file.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Wrapper around ProcessSingleFileCompletelyAsync that includes UI update callbacks
    /// </summary>
    private async Task ProcessSingleFileWithUIUpdatesAsync(AudioFile file, MovieSession session, Action<string, int>? progressCallback = null)
    {
        // Process the file with UI updates
        await ProcessSingleFileCompletelyAsync(file, session, (message, progress) =>
        {
            // Update the specific file's progress and trigger UI refresh
            progressCallback?.Invoke($"Processing {Path.GetFileName(file.FilePath)}: {message}", progress);
        });
    }

    /// <summary>
    /// Processes a single file through ALL individual stages continuously: conversion → upload → transcription → JSON download.
    /// This runs completely independently for each file until transcription is downloaded.
    /// Each stage flows immediately into the next without waiting.
    /// </summary>
    private async Task ProcessSingleFileCompletelyAsync(AudioFile file, MovieSession session, Action<string, int>? progressCallback = null)
    {
        try
        {
            string fileName = Path.GetFileName(file.FilePath);
            _logger.LogInformation("Starting complete processing for file {FileName} with initial status {Status}",
                fileName, file.ProcessingStatus);

            // STAGE 1: CONVERSION (if needed)
            if (file.ProcessingStatus == AudioProcessingStatus.Pending)
            {
                if (Path.GetExtension(file.FilePath).ToLowerInvariant() == ".wav")
                {
                    _logger.LogDebug("File {FileName} - Converting WAV to MP3", fileName);
                    await ConvertSingleFileAsync(file, session.FolderPath, progressCallback);

                    // Check if conversion failed
                    if (file.ProcessingStatus == AudioProcessingStatus.FailedMp3 ||
                        file.ProcessingStatus == AudioProcessingStatus.Failed)
                    {
                        _logger.LogError("File {FileName} - Conversion failed with status {Status}", fileName, file.ProcessingStatus);
                        return;
                    }
                }
                else if (Path.GetExtension(file.FilePath).ToLowerInvariant() == ".mp3")
                {
                    _logger.LogDebug("File {FileName} - Already MP3, marking ready for upload", fileName);
                    file.ProcessingStatus = AudioProcessingStatus.FinishedConvertingToMp3;
                    file.CurrentStep = "MP3 ready, starting upload...";
                    file.ProgressPercentage = 100;
                    file.LastUpdated = DateTime.UtcNow;
                }
                else
                {
                    file.ProcessingStatus = AudioProcessingStatus.Failed;
                    file.ConversionError = $"Unsupported file type: {Path.GetExtension(file.FilePath)}";
                    file.CurrentStep = "Unsupported file type";
                    return;
                }
            }

            // STAGE 2: UPLOAD TO GLADIA (immediately after conversion) - AUTOMATIC FLOW
            if (file.ProcessingStatus == AudioProcessingStatus.FinishedConvertingToMp3)
            {
                _logger.LogDebug("File {FileName} - Automatically starting upload after conversion", fileName);
                // Reset progress for upload phase
                file.ProgressPercentage = 0;
                file.CurrentStep = "Starting upload to Gladia...";
                file.LastUpdated = DateTime.UtcNow;

                // Small delay to show the transition in UI
                await Task.Delay(100);

                await UploadSingleFileAsync(file, session, progressCallback);

                if (file.ProcessingStatus == AudioProcessingStatus.Failed)
                {
                    _logger.LogError("File {FileName} - Upload failed", fileName);
                    return;
                }
            }

            // STAGE 3: TRANSCRIPTION (immediately after upload) - AUTOMATIC FLOW
            if (file.ProcessingStatus == AudioProcessingStatus.FinishedUploadingToGladia)
            {
                _logger.LogDebug("File {FileName} - Automatically starting transcription after upload", fileName);
                // Reset progress for transcription phase
                file.ProgressPercentage = 0;
                file.CurrentStep = "Starting transcription...";
                file.LastUpdated = DateTime.UtcNow;

                // Small delay to show the transition in UI
                await Task.Delay(100);

                await ProcessSingleFileTranscriptionAsync(file, session);
            }

            _logger.LogInformation("File {FileName} completed processing with final status {Status}",
                fileName, file.ProcessingStatus);
        }
        catch (Exception ex)
        {
            file.ProcessingStatus = AudioProcessingStatus.Failed;
            file.ConversionError = ex.Message;
            file.CurrentStep = $"Processing failed: {ex.Message}";
            file.ProgressPercentage = 0;
            file.CanRetry = true;
            file.LastUpdated = DateTime.UtcNow;

            _logger.LogError(ex, "Failed to process file {FileName}", Path.GetFileName(file.FilePath));
        }
    }

    /// <summary>
    /// Converts a single WAV file to MP3.
    /// </summary>
    private async Task ConvertSingleFileAsync(AudioFile file, string sessionFolderPath, Action<string, int>? progressCallback = null)
    {
        file.ProcessingStatus = AudioProcessingStatus.ConvertingToMp3;
        file.CurrentStep = "Converting to MP3...";
        file.ProgressPercentage = 0;
        file.LastUpdated = DateTime.UtcNow;

        try
        {
            List<(AudioFile audioFile, bool success, string? error)> results = await _gladiaService.ConvertAllWavsToMp3Async(
                new List<AudioFile> { file },
                sessionFolderPath,
                (step, current, total) =>
                {
                    int newProgress = (int)((double)current / total * 100);
                    file.ProgressPercentage = newProgress;
                    file.CurrentStep = $"Converting to MP3... {newProgress}%";
                    file.LastUpdated = DateTime.UtcNow;
                    _logger.LogDebug("File {FileName} conversion progress: {Progress}% - {Step}",
                        Path.GetFileName(file.FilePath), newProgress, step);
                });

            (AudioFile audioFile, bool success, string? error) result = results.FirstOrDefault();
            if (result.success)
            {
                file.ProcessingStatus = AudioProcessingStatus.FinishedConvertingToMp3;
                file.CurrentStep = "Conversion complete";
                file.ProgressPercentage = 100;
                file.CanRetry = true;
                file.LastUpdated = DateTime.UtcNow;
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
    }

    /// <summary>
    /// Uploads a single file to Gladia.
    /// </summary>
    private async Task UploadSingleFileAsync(AudioFile file, MovieSession session, Action<string, int>? progressCallback = null)
    {
        if (!string.IsNullOrEmpty(file.AudioUrl))
        {
            // Already uploaded
            return;
        }

        file.ProcessingStatus = AudioProcessingStatus.UploadingToGladia;
        file.CurrentStep = "Uploading to Gladia...";
        file.ProgressPercentage = 0;
        file.LastUpdated = DateTime.UtcNow;

        try
        {
            List<(AudioFile audioFile, bool success, string? error)> results = await _gladiaService.UploadAllMp3sToGladiaAsync(
                new List<AudioFile> { file },
                session.FolderPath,
                (step, current, total) =>
                {
                    int newProgress = (int)((double)current / total * 100);
                    file.ProgressPercentage = newProgress;
                    file.CurrentStep = $"Uploading to Gladia... {newProgress}%";
                    file.LastUpdated = DateTime.UtcNow;
                    _logger.LogDebug("File {FileName} upload progress: {Progress}% - {Step}",
                        Path.GetFileName(file.FilePath), newProgress, step);
                },
                session,
                null);

            (AudioFile audioFile, bool success, string? error) result = results.FirstOrDefault();
            if (result.success)
            {
                file.ProcessingStatus = AudioProcessingStatus.FinishedUploadingToGladia;
                file.CurrentStep = "Upload complete, ready for transcription";
                file.ProgressPercentage = 100;
                file.CanRetry = true;
                file.LastUpdated = DateTime.UtcNow;
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
            file.CurrentStep = "Converting to MP3...";
            file.ProgressPercentage = 0;
            file.LastUpdated = DateTime.UtcNow;

            // Notify UI about the conversion starting
            progressCallback?.Invoke($"Converting {Path.GetFileName(file.FilePath)} to MP3", 20 + (i * 20 / wavFiles.Count));

            try
            {
                List<(AudioFile audioFile, bool success, string? error)> results = await _gladiaService.ConvertAllWavsToMp3Async(
                    new List<AudioFile> { file },
                    session.FolderPath,
                    (step, current, total) =>
                    {
                        file.ProgressPercentage = (int)((double)current / total * 100);
                        file.CurrentStep = step;
                        file.LastUpdated = DateTime.UtcNow;
                        // Also update the progress callback with the specific file info
                        progressCallback?.Invoke($"Converting {Path.GetFileName(file.FilePath)}: {step}", 20 + (i * 20 / wavFiles.Count) + (current * 20 / (total * wavFiles.Count)));
                    });

                (AudioFile audioFile, bool success, string? error) result = results.FirstOrDefault();
                if (result.success)
                {
                    file.ProcessingStatus = AudioProcessingStatus.FinishedConvertingToMp3;
                    file.CurrentStep = "Conversion complete, ready to upload";
                    file.ProgressPercentage = 100;
                    file.CanRetry = true;
                    file.LastUpdated = DateTime.UtcNow;
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
            f.ProcessingStatus == AudioProcessingStatus.FinishedConvertingToMp3 &&
            string.IsNullOrEmpty(f.AudioUrl)).ToList();

        if (!filesToUpload.Any()) return;

        progressCallback?.Invoke("Uploading files to Gladia", 40);

        for (int i = 0; i < filesToUpload.Count; i++)
        {
            AudioFile file = filesToUpload[i];
            file.ProcessingStatus = AudioProcessingStatus.UploadingToGladia;
            file.CurrentStep = "Uploading to Gladia...";
            file.ProgressPercentage = 0;
            file.LastUpdated = DateTime.UtcNow;

            // Notify UI about the upload starting
            progressCallback?.Invoke($"Uploading {Path.GetFileName(file.FilePath)} to Gladia", 40 + (i * 20 / filesToUpload.Count));

            try
            {
                List<(AudioFile audioFile, bool success, string? error)> results = await _gladiaService.UploadAllMp3sToGladiaAsync(
                    new List<AudioFile> { file },
                    session.FolderPath,
                    (step, current, total) =>
                    {
                        file.ProgressPercentage = (int)((double)current / total * 100);
                        file.CurrentStep = step;
                        file.LastUpdated = DateTime.UtcNow;
                        // Also update the progress callback with the specific file info
                        progressCallback?.Invoke($"Uploading {Path.GetFileName(file.FilePath)}: {step}", 40 + (i * 20 / filesToUpload.Count) + (current * 20 / (total * filesToUpload.Count)));
                    },
                    session,
                    null); // No save callback for this service

                (AudioFile audioFile, bool success, string? error) result = results.FirstOrDefault();
                if (result.success)
                {
                    file.ProcessingStatus = AudioProcessingStatus.FinishedUploadingToGladia;
                    file.CurrentStep = "Waiting for transcription to start";
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
    /// Processes a single file's transcription completely: starts transcription, waits for completion, downloads JSON.
    /// This runs independently for each file in parallel.
    /// </summary>
    private async Task ProcessSingleFileTranscriptionAsync(AudioFile file, MovieSession session)
    {
        try
        {
            string fileName = Path.GetFileName(file.FilePath);
            _logger.LogInformation("Starting transcription for file {FileName} with AudioUrl: {AudioUrl}",
                fileName, !string.IsNullOrEmpty(file.AudioUrl) ? "SET" : "EMPTY");

            if (string.IsNullOrEmpty(file.AudioUrl))
            {
                throw new InvalidOperationException($"File {fileName} has no AudioUrl - cannot start transcription");
            }

            file.ProcessingStatus = AudioProcessingStatus.WaitingToDownloadTranscripts;
            file.CurrentStep = "Starting transcription...";
            file.ProgressPercentage = 0;
            file.LastUpdated = DateTime.UtcNow;

            // Start transcription
            int numSpeakers = session.MicAssignments?.Count ?? 2;
            _logger.LogDebug("Starting transcription for {FileName} with {NumSpeakers} speakers", fileName, numSpeakers);

            string transcriptionId = await _gladiaService.StartTranscriptionAsync(
                file.AudioUrl!, numSpeakers, true, file.FileName);

            _logger.LogInformation("Transcription started for {FileName} with ID: {TranscriptionId}", fileName, transcriptionId);

            file.TranscriptId = transcriptionId;
            file.ProcessingStatus = AudioProcessingStatus.DownloadingTranscripts;
            file.CurrentStep = "Waiting for transcription to complete...";
            file.ProgressPercentage = 25;
            file.LastUpdated = DateTime.UtcNow;

            // Wait for completion and download results
            _logger.LogDebug("Waiting for transcription completion: {TranscriptionId}", transcriptionId);
            dynamic result = await _gladiaService.WaitForTranscriptionAsync(transcriptionId);

            file.CurrentStep = "Processing transcript data...";
            file.ProgressPercentage = 75;
            file.LastUpdated = DateTime.UtcNow;

            // Apply speaker mapping
            string? rawTranscript = result.result?.transcription?.full_transcript;
            _logger.LogInformation("Raw transcript received for {FileName}: {Length} characters",
                fileName, rawTranscript?.Length ?? 0);

            file.TranscriptText = session.MicAssignments != null && !string.IsNullOrEmpty(rawTranscript)
                ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, session.MicAssignments, file.FileName)
                : rawTranscript;

            file.ProcessingStatus = AudioProcessingStatus.TranscriptsDownloaded;
            file.CurrentStep = "Transcription complete, waiting for other files";
            file.ProgressPercentage = 100;
            file.ProcessedAt = DateTime.UtcNow;
            file.CanRetry = true;
            file.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("File {FileName} transcription completed successfully with {Length} characters",
                fileName, file.TranscriptText?.Length ?? 0);
        }
        catch (Exception ex)
        {
            file.ProcessingStatus = AudioProcessingStatus.Failed;
            file.ConversionError = ex.Message;
            file.CurrentStep = $"Transcription failed: {ex.Message}";
            file.CanRetry = true;
            file.LastUpdated = DateTime.UtcNow;

            _logger.LogError(ex, "Failed to transcribe file {FileName}: {Error}", Path.GetFileName(file.FilePath), ex.Message);
        }
    }

    /// <summary>
    /// Validates that ALL files have completed transcription and moves them to synchronous processing.
    /// This is the synchronization point where parallel processing ends and synchronous processing begins.
    /// </summary>
    private async Task ValidateAllFilesTranscribedAndProceed(MovieSession session, Action<string, int>? progressCallback = null)
    {
        _logger.LogInformation("Validating that all {FileCount} files have completed transcription for session {SessionId}",
            session.AudioFiles.Count, session.Id);

        List<AudioFile> allFiles = session.AudioFiles.ToList();
        List<AudioFile> transcribedFiles = allFiles.Where(f =>
            f.ProcessingStatus == AudioProcessingStatus.TranscriptsDownloaded &&
            !string.IsNullOrEmpty(f.TranscriptText)).ToList();

        List<AudioFile> pendingFiles = allFiles.Where(f =>
            f.ProcessingStatus != AudioProcessingStatus.TranscriptsDownloaded ||
            string.IsNullOrEmpty(f.TranscriptText)).ToList();

        _logger.LogInformation("Transcription status: {TranscribedCount}/{TotalCount} files completed. {PendingCount} still pending.",
            transcribedFiles.Count, allFiles.Count, pendingFiles.Count);

        if (pendingFiles.Any())
        {
            // Some files haven't completed transcription yet
            foreach (AudioFile pendingFile in pendingFiles)
            {
                _logger.LogWarning("File {FileName} not ready: Status={Status}, HasTranscript={HasTranscript}",
                    pendingFile.FileName, pendingFile.ProcessingStatus, !string.IsNullOrEmpty(pendingFile.TranscriptText));
            }

            // Mark transcribed files as waiting
            foreach (AudioFile file in transcribedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.WaitingForOtherFiles;
                file.CurrentStep = $"Waiting for {pendingFiles.Count} other files to complete transcription";
                file.LastUpdated = DateTime.UtcNow;
            }

            string pendingFileNames = string.Join(", ", pendingFiles.Select(f => f.FileName));
            string message = $"Waiting for {pendingFiles.Count} files to complete transcription: {pendingFileNames}";
            progressCallback?.Invoke(message, 85);

            throw new InvalidOperationException($"Cannot proceed to combined transcription processing: {pendingFiles.Count} files still need transcription completion");
        }

        // ALL files are transcribed - proceed to synchronous combined processing
        _logger.LogInformation("All {FileCount} files have completed individual transcription. Proceeding to synchronous processing.",
            transcribedFiles.Count);

        foreach (AudioFile file in transcribedFiles)
        {
            file.ProcessingStatus = AudioProcessingStatus.ProcessingTranscriptions;
            file.CurrentStep = "Processing all transcriptions together";
            file.LastUpdated = DateTime.UtcNow;
        }

        progressCallback?.Invoke("All transcriptions complete - starting combined analysis", 90);
    }

    /// <summary>
    /// Downloads transcription JSON files from Gladia and processes them exactly like the Speaker Attribution Fix page.
    /// This mimics: Download JSONs → Analyze Files → Fix Speaker Attribution → Create master_mix_with_speakers.json
    /// </summary>
    public async Task ProcessTranscriptionsFromUploadedFilesAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Starting transcription processing workflow", 5);

        try
        {
            // Step 1: Download all JSON files from Gladia (like downloading transcripts)
            await DownloadTranscriptsAsync(session, progressCallback);

            // Step 2: Use SpeakerAttributionFixService to do the full workflow
            // This mimics clicking "Analyze Files" then "Fix Speaker Attribution" on the Speaker fix page
            await RunSpeakerAttributionWorkflow(session, progressCallback);

            progressCallback?.Invoke("Transcription processing complete", 100);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process transcriptions for session {SessionId}", session.Id);
            progressCallback?.Invoke($"Processing failed: {ex.Message}", 0);
            throw;
        }
    }

    /// <summary>
    /// Runs the complete Speaker Attribution Fix workflow using the existing SpeakerAttributionFixService.
    /// This does: Analyze Files → Fix Speaker Attribution → Create master_mix_with_speakers.json
    /// </summary>
    private async Task RunSpeakerAttributionWorkflow(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Running speaker attribution analysis and fix", 75);

        // We need to inject the SpeakerAttributionFixService - for now we'll create it manually
        // In a real DI scenario, this would be injected
        ILogger<SpeakerAttributionFixService> speakerFixLogger = new LoggerFactory().CreateLogger<SpeakerAttributionFixService>();
        SpeakerAttributionFixService speakerFixService = new SpeakerAttributionFixService(speakerFixLogger);

        try
        {
            // Step 1: Analyze transcription files (like clicking "Analyze Files")
            _logger.LogInformation("Analyzing transcription files for session {SessionId}", session.Id);
            var analysisReport = await speakerFixService.AnalyzeTranscriptionFiles(session.FolderPath);

            if (!analysisReport.MasterMixFound)
            {
                _logger.LogWarning("No master mix file found for speaker attribution fix");
                progressCallback?.Invoke("No master mix file found for speaker attribution", 85);
                return;
            }

            // Step 2: Fix speaker attribution (like clicking "Fix Speaker Attribution")
            _logger.LogInformation("Running speaker attribution fix for session {SessionId}", session.Id);
            var fixResult = await speakerFixService.FixSpeakerAttributionForSession(session);

            if (fixResult.Success)
            {
                _logger.LogInformation("Speaker attribution fix completed successfully. Output: {OutputFile}", 
                    fixResult.OutputFilePath);

                // Step 3: Extract and save session statistics from the fix result
                ExtractSessionStatisticsFromFixResult(session, fixResult);
                
                progressCallback?.Invoke("Speaker attribution and statistics extraction completed", 95);
            }
            else
            {
                _logger.LogError("Speaker attribution fix failed: {Error}", fixResult.ErrorMessage);
                progressCallback?.Invoke($"Speaker attribution fix failed: {fixResult.ErrorMessage}", 85);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run speaker attribution workflow");
            progressCallback?.Invoke($"Speaker attribution workflow failed: {ex.Message}", 85);
            throw;
        }
    }

    /// <summary>
    /// Extracts conversation statistics from the SpeakerAttributionResult and populates the session's SessionStats.
    /// Maps the detailed statistics generated during speaker attribution to the session model.
    /// </summary>
    private void ExtractSessionStatisticsFromFixResult(MovieSession session, object fixResult)
    {
        try
        {
            // Use reflection to access the properties since we don't have direct access to the SpeakerAttributionResult type
            var fixResultType = fixResult.GetType();
            
            // Initialize SessionStats if not already done
            if (session.SessionStats == null)
            {
                session.SessionStats = new SessionStats();
            }

            // Extract word counts per speaker
            var wordCountsProperty = fixResultType.GetProperty("WordCountsPerSpeaker");
            if (wordCountsProperty?.GetValue(fixResult) is Dictionary<string, int> wordCounts)
            {
                session.SessionStats.WordCounts = wordCounts;
                _logger.LogDebug("Extracted word counts for {SpeakerCount} speakers", wordCounts.Count);
            }

            // Extract question counts per speaker
            var questionCountsProperty = fixResultType.GetProperty("QuestionCountsPerSpeaker");
            if (questionCountsProperty?.GetValue(fixResult) is Dictionary<string, int> questionCounts)
            {
                session.SessionStats.QuestionCounts = questionCounts;
                session.SessionStats.TotalQuestions = questionCounts.Values.Sum();
                _logger.LogDebug("Extracted question counts: {TotalQuestions} total questions", session.SessionStats.TotalQuestions);
            }

            // Extract laughter counts per speaker
            var laughterCountsProperty = fixResultType.GetProperty("LaughterCountsPerSpeaker");
            if (laughterCountsProperty?.GetValue(fixResult) is Dictionary<string, int> laughterCounts)
            {
                session.SessionStats.LaughterCounts = laughterCounts;
                session.SessionStats.TotalLaughterMoments = laughterCounts.Values.Sum();
                _logger.LogDebug("Extracted laughter counts: {TotalLaughter} total moments", session.SessionStats.TotalLaughterMoments);
            }

            // Extract curse word counts per speaker
            var curseWordCountsProperty = fixResultType.GetProperty("CurseWordCountsPerSpeaker");
            if (curseWordCountsProperty?.GetValue(fixResult) is Dictionary<string, int> curseCounts)
            {
                session.SessionStats.CurseWordCounts = curseCounts;
                session.SessionStats.TotalCurseWords = curseCounts.Values.Sum();
                _logger.LogDebug("Extracted curse word counts: {TotalCurseWords} total curse words", session.SessionStats.TotalCurseWords);
            }

            // Extract detailed curse words per speaker
            var curseWordsPerSpeakerProperty = fixResultType.GetProperty("CurseWordsPerSpeaker");
            if (curseWordsPerSpeakerProperty?.GetValue(fixResult) is Dictionary<string, List<string>> detailedCurseWords)
            {
                session.SessionStats.DetailedCurseWords = ExtractDetailedWordUsage(detailedCurseWords);
                _logger.LogDebug("Extracted detailed curse words for {SpeakerCount} speakers", detailedCurseWords.Count);
            }

            // Extract pejorative word counts per speaker
            var pejorativeCountsProperty = fixResultType.GetProperty("PejorativeCountsPerSpeaker");
            if (pejorativeCountsProperty?.GetValue(fixResult) is Dictionary<string, int> pejorativeCounts)
            {
                session.SessionStats.PejorativeWordCounts = pejorativeCounts;
                session.SessionStats.TotalPejorativeWords = pejorativeCounts.Values.Sum();
                _logger.LogDebug("Extracted pejorative word counts: {TotalPejorativeWords} total pejorative words", session.SessionStats.TotalPejorativeWords);
            }

            // Extract detailed pejorative words per speaker
            var pejorativeWordsPerSpeakerProperty = fixResultType.GetProperty("PejorativeWordsPerSpeaker");
            if (pejorativeWordsPerSpeakerProperty?.GetValue(fixResult) is Dictionary<string, List<string>> detailedPejorativeWords)
            {
                session.SessionStats.DetailedPejorativeWords = ExtractDetailedWordUsage(detailedPejorativeWords);
                _logger.LogDebug("Extracted detailed pejorative words for {SpeakerCount} speakers", detailedPejorativeWords.Count);
            }

            // Extract interruption counts per speaker (if available)
            var interruptionCountsProperty = fixResultType.GetProperty("InterruptionCountsPerSpeaker");
            if (interruptionCountsProperty?.GetValue(fixResult) is Dictionary<string, int> interruptionCounts)
            {
                session.SessionStats.InterruptionCounts = interruptionCounts;
                session.SessionStats.TotalInterruptions = interruptionCounts.Values.Sum();
                _logger.LogDebug("Extracted interruption counts: {TotalInterruptions} total interruptions", session.SessionStats.TotalInterruptions);
            }

            // Extract conversation tone
            var conversationToneProperty = fixResultType.GetProperty("ConversationTone");
            if (conversationToneProperty?.GetValue(fixResult) is string conversationTone)
            {
                session.SessionStats.ConversationTone = conversationTone;
            }

            // Calculate most talkative, quietest, etc. based on word counts
            if (session.SessionStats.WordCounts.Any())
            {
                var mostTalkative = session.SessionStats.WordCounts.OrderByDescending(kvp => kvp.Value).First();
                session.SessionStats.MostTalkativePerson = mostTalkative.Key;

                var quietest = session.SessionStats.WordCounts.OrderBy(kvp => kvp.Value).First();
                session.SessionStats.QuietestPerson = quietest.Key;
            }

            // Calculate most inquisitive person based on question counts
            if (session.SessionStats.QuestionCounts.Any())
            {
                var mostInquisitive = session.SessionStats.QuestionCounts.OrderByDescending(kvp => kvp.Value).First();
                session.SessionStats.MostInquisitivePerson = mostInquisitive.Key;
            }

            // Calculate funniest person based on laughter counts
            if (session.SessionStats.LaughterCounts.Any())
            {
                var funniest = session.SessionStats.LaughterCounts.OrderByDescending(kvp => kvp.Value).First();
                session.SessionStats.FunniestPerson = funniest.Key;
            }

            // Calculate most profane person based on curse word counts
            if (session.SessionStats.CurseWordCounts.Any())
            {
                var mostProfane = session.SessionStats.CurseWordCounts.OrderByDescending(kvp => kvp.Value).First();
                session.SessionStats.MostProfanePerson = mostProfane.Key;
            }

            // Calculate biggest interruptor based on interruption counts
            if (session.SessionStats.InterruptionCounts.Any())
            {
                var biggestInterruptor = session.SessionStats.InterruptionCounts.OrderByDescending(kvp => kvp.Value).First();
                session.SessionStats.BiggestInterruptor = biggestInterruptor.Key;
            }

            // Calculate most pejorative person based on pejorative word counts
            if (session.SessionStats.PejorativeWordCounts.Any())
            {
                var mostPejorative = session.SessionStats.PejorativeWordCounts.OrderByDescending(kvp => kvp.Value).First();
                session.SessionStats.MostPejorativePerson = mostPejorative.Key;
            }

            _logger.LogInformation("Successfully extracted session statistics for session {SessionId}", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract session statistics from fix result for session {SessionId}", session.Id);
            // Don't throw - this is not critical enough to fail the entire process
        }
    }

    /// <summary>
    /// Converts the dictionary of words per speaker into DetailedWordUsage objects.
    /// Groups words by speaker and creates usage statistics with context.
    /// </summary>
    private List<DetailedWordUsage> ExtractDetailedWordUsage(Dictionary<string, List<string>> wordsPerSpeaker)
    {
        List<DetailedWordUsage> detailedUsage = new();

        foreach (var speakerWords in wordsPerSpeaker)
        {
            string speaker = speakerWords.Key;
            List<string> words = speakerWords.Value;

            // Group words and count occurrences
            var wordGroups = words.GroupBy(w => w.ToLowerInvariant())
                                 .OrderByDescending(g => g.Count())
                                 .ThenBy(g => g.Key);

            foreach (var wordGroup in wordGroups)
            {
                string word = wordGroup.Key;
                int count = wordGroup.Count();
                
                // Take first few examples as context (limit to avoid too much data)
                List<string> contextExamples = wordGroup.Take(3).ToList();

                detailedUsage.Add(new DetailedWordUsage
                {
                    Word = word,
                    Speaker = speaker,
                    Count = count,
                    ContextExamples = contextExamples
                });
            }
        }

        // Sort by speaker name, then by count descending
        return detailedUsage.OrderBy(d => d.Speaker)
                           .ThenByDescending(d => d.Count)
                           .ToList();
    }

    /// <summary>
    /// Downloads transcription JSON files very fast in parallel for all uploaded files.
    /// </summary>
    public async Task DownloadTranscriptsAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Starting transcript downloads", 10);

        // Find files that need JSON downloads - download all files with AudioUrl (overwrite existing)
        List<AudioFile> filesToDownload = session.AudioFiles.Where(f =>
            !string.IsNullOrEmpty(f.AudioUrl)).ToList();

        _logger.LogInformation("Found {FileCount} files that need JSON downloads", filesToDownload.Count);

        if (!filesToDownload.Any())
        {
            _logger.LogWarning("No files found that need transcript downloads");
            progressCallback?.Invoke("No files need transcript downloads", 50);
            return;
        }

        progressCallback?.Invoke($"Downloading transcripts for {filesToDownload.Count} files in parallel", 20);

        // Download all JSON files in parallel
        List<Task> downloadTasks = new List<Task>();

        foreach (AudioFile file in filesToDownload)
        {
            Task downloadTask = DownloadSingleTranscriptAsync(file, session);
            downloadTasks.Add(downloadTask);
        }

        // Wait for all downloads to complete
        await Task.WhenAll(downloadTasks);

        int successCount = filesToDownload.Count(f => f.ProcessingStatus == AudioProcessingStatus.TranscriptsDownloaded);
        int failCount = filesToDownload.Count - successCount;

        progressCallback?.Invoke($"Downloaded {successCount}/{filesToDownload.Count} transcripts successfully", 70);
        _logger.LogInformation("Completed transcript downloads: {SuccessCount} successful, {FailCount} failed",
            successCount, failCount);
    }

    /// <summary>
    /// Downloads a single transcript JSON file for an already processed transcription.
    /// </summary>
    private async Task DownloadSingleTranscriptAsync(AudioFile file, MovieSession session)
    {
        try
        {
            string fileName = Path.GetFileName(file.FilePath);
            _logger.LogDebug("Downloading existing transcript for {FileName} with ID {TranscriptId}", fileName, file.TranscriptId);

            file.ProcessingStatus = AudioProcessingStatus.DownloadingTranscripts;
            file.CurrentStep = "Downloading transcript JSON...";
            file.ProgressPercentage = 10;
            file.LastUpdated = DateTime.UtcNow;

            // Check if we have a TranscriptId - if not, we can't download
            if (string.IsNullOrEmpty(file.TranscriptId))
            {
                throw new InvalidOperationException($"File {fileName} has no TranscriptId - cannot download transcript");
            }

            file.CurrentStep = "Fetching transcript data from Gladia...";
            file.ProgressPercentage = 30;
            file.LastUpdated = DateTime.UtcNow;

            // Download the existing transcription result using the ID
            TranscriptionResult result = await _gladiaService.GetTranscriptionResultAsync(file.TranscriptId);

            if (result.status != "done")
            {
                throw new InvalidOperationException($"Transcription {file.TranscriptId} is not complete (status: {result.status})");
            }

            file.CurrentStep = "Saving JSON file...";
            file.ProgressPercentage = 70;
            file.LastUpdated = DateTime.UtcNow;

            // Save the JSON file to disk
            string jsonPath = await _gladiaService.SaveTranscriptionJsonAsync(result, file.FilePath, session.FolderPath);

            // Store the raw transcript and JSON path
            string? rawTranscript = result.result?.transcription?.full_transcript;
            file.TranscriptText = rawTranscript; // Store raw transcript for now
            file.JsonFilePath = jsonPath;
            file.ProcessingStatus = AudioProcessingStatus.TranscriptsDownloaded;
            file.CurrentStep = "Transcript downloaded";
            file.ProgressPercentage = 100;
            file.ProcessedAt = DateTime.UtcNow;
            file.CanRetry = true;
            file.LastUpdated = DateTime.UtcNow;

            _logger.LogDebug("Successfully downloaded transcript for {FileName} to {JsonPath}", fileName, jsonPath);
        }
        catch (Exception ex)
        {
            file.ProcessingStatus = AudioProcessingStatus.Failed;
            file.ConversionError = ex.Message;
            file.CurrentStep = $"Download failed: {ex.Message}";
            file.CanRetry = true;
            file.LastUpdated = DateTime.UtcNow;

            _logger.LogError(ex, "Failed to download transcript for {FileName}", Path.GetFileName(file.FilePath));
        }
    }

    /// <summary>
    /// Maps speaker labels to names in JSON files using proper JSON processing.
    /// Works with the actual JSON utterance data, not the unreliable TranscriptText backup.
    /// Similar to what's done on the Speaker Attribution Fix page.
    /// </summary>
    public async Task MergeMicTranscriptToNamesAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        progressCallback?.Invoke("Processing JSON files for speaker mapping", 80);

        List<AudioFile> filesWithJsons = session.AudioFiles.Where(f =>
            !string.IsNullOrEmpty(f.JsonFilePath) &&
            File.Exists(f.JsonFilePath)).ToList();

        if (!filesWithJsons.Any())
        {
            _logger.LogWarning("No files with JSON transcripts found for speaker mapping");
            progressCallback?.Invoke("No JSON files to process", 90);
            return;
        }

        if (session.MicAssignments == null || !session.MicAssignments.Any())
        {
            _logger.LogWarning("No mic assignments available for speaker mapping");
            progressCallback?.Invoke("No mic assignments available", 90);
            return;
        }

        _logger.LogInformation("Processing JSON files for speaker mapping: {FileCount} files", filesWithJsons.Count);

        foreach (AudioFile file in filesWithJsons)
        {
            try
            {
                await ProcessSingleJsonFileForSpeakerMapping(file, session.MicAssignments);
                file.CurrentStep = "Speaker mapping complete";
                file.LastUpdated = DateTime.UtcNow;

                _logger.LogDebug("Processed JSON speaker mapping for {FileName}", file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process JSON speaker mapping for {FileName}", file.FileName);
                // Don't fail the whole process, just log the error
            }
        }

        progressCallback?.Invoke("All JSON speaker mapping complete", 100);
        _logger.LogInformation("Completed JSON speaker mapping for {FileCount} files", filesWithJsons.Count);
    }

    /// <summary>
    /// Processes a single JSON transcription file to apply proper speaker names to utterances.
    /// Based on the Speaker Attribution Fix logic but simplified for individual files.
    /// </summary>
    private async Task ProcessSingleJsonFileForSpeakerMapping(AudioFile file, Dictionary<int, string> micAssignments)
    {
        try
        {
            // Read the JSON file
            string jsonContent = await File.ReadAllTextAsync(file.JsonFilePath!);

            // Deserialize using the same structure as Speaker Attribution Fix
            dynamic? transcriptionData = System.Text.Json.JsonSerializer.Deserialize<dynamic>(jsonContent);
            if (transcriptionData == null)
            {
                _logger.LogWarning("Failed to parse JSON for {FileName}", file.FileName);
                return;
            }

            // Check if this is an individual mic file
            string upperFileName = file.FileName.ToUpperInvariant();
            System.Text.RegularExpressions.Match micMatch = System.Text.RegularExpressions.Regex.Match(upperFileName, @"^MIC(\d+)\.(WAV|MP3)$");

            if (micMatch.Success)
            {
                // Individual mic file - all utterances belong to one person
                int fileBasedMicNumber = int.Parse(micMatch.Groups[1].Value); // 1-based from filename
                int micNumber = fileBasedMicNumber - 1; // Convert to 0-based for assignments lookup

                if (micAssignments.TryGetValue(micNumber, out string? participantName) && !string.IsNullOrEmpty(participantName))
                {
                    _logger.LogInformation("Mapping MIC{FileNumber} (assignment key {MicNumber}) to {ParticipantName}",
                        fileBasedMicNumber, micNumber, participantName);

                    // Build proper transcript text with speaker name for each utterance
                    await BuildTranscriptFromJsonWithSpeakerName(file, participantName, jsonContent);
                }
                else
                {
                    _logger.LogWarning("No participant name found for MIC{FileNumber} (assignment key {MicNumber})",
                        fileBasedMicNumber, micNumber);
                }
            }
            else
            {
                // For master mix or other files, use the existing mapping logic
                file.TranscriptText = _gladiaService.MapSpeakerLabelsToNames(
                    file.TranscriptText ?? "", micAssignments, file.FileName);

                _logger.LogDebug("Applied speaker label mapping for non-mic file {FileName}", file.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JSON file {JsonPath} for {FileName}", file.JsonFilePath, file.FileName);
            throw;
        }
    }

    /// <summary>
    /// Builds a proper transcript text from JSON utterances with the correct speaker name.
    /// </summary>
    private async Task BuildTranscriptFromJsonWithSpeakerName(AudioFile file, string speakerName, string jsonContent)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("result", out JsonElement result) &&
                result.TryGetProperty("transcription", out JsonElement transcription) &&
                transcription.TryGetProperty("utterances", out JsonElement utterances))
            {
                StringBuilder transcriptBuilder = new StringBuilder();

                foreach (JsonElement utterance in utterances.EnumerateArray())
                {
                    if (utterance.TryGetProperty("text", out JsonElement textElement))
                    {
                        string? text = textElement.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            transcriptBuilder.AppendLine($"{speakerName}: {text.Trim()}");
                        }
                    }
                }

                // Update the transcript text with properly named speakers
                string newTranscript = transcriptBuilder.ToString();
                file.TranscriptText = newTranscript;

                _logger.LogDebug("Built transcript with {LineCount} lines for {SpeakerName} from {FileName}",
                    newTranscript.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, speakerName, file.FileName);
            }
            else
            {
                _logger.LogWarning("JSON structure not as expected for {FileName}", file.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build transcript from JSON for {FileName}", file.FileName);
            throw;
        }
    }

    /// <summary>
    /// Legacy method - Processes transcriptions for uploaded audio files using the Gladia service.
    /// This is kept for backward compatibility but the new parallel approach should be used instead.
    /// </summary>
    public async Task ProcessTranscriptionsAsync(MovieSession session, Action<string, int>? progressCallback = null)
    {
        // Debug: Log all files and their status before filtering
        Console.WriteLine($"DEBUG TRANSCRIPTION: ProcessTranscriptionsAsync called with {session.AudioFiles.Count} files:");
        foreach (var file in session.AudioFiles)
        {
            Console.WriteLine($"  File: {file.FileName}");
            Console.WriteLine($"    Status: {file.ProcessingStatus}");
            Console.WriteLine($"    AudioUrl: {(string.IsNullOrEmpty(file.AudioUrl) ? "EMPTY" : "SET")}");
            Console.WriteLine($"    TranscriptText: {(string.IsNullOrEmpty(file.TranscriptText) ? "EMPTY" : "HAS_TEXT")}");
        }

        List<AudioFile> filesToTranscribe = session.AudioFiles.Where(f =>
            f.ProcessingStatus == AudioProcessingStatus.FinishedUploadingToGladia &&
            !string.IsNullOrEmpty(f.AudioUrl) &&
            string.IsNullOrEmpty(f.TranscriptText)).ToList();

        Console.WriteLine($"DEBUG TRANSCRIPTION: After filtering, {filesToTranscribe.Count} files qualify for transcription:");
        foreach (var file in filesToTranscribe)
        {
            Console.WriteLine($"  - {file.FileName}");
        }

        if (!filesToTranscribe.Any())
        {
            Console.WriteLine("DEBUG TRANSCRIPTION: No files to transcribe, returning early");
            return;
        }

        progressCallback?.Invoke("Processing transcriptions", 60);

        for (int i = 0; i < filesToTranscribe.Count; i++)
        {
            AudioFile file = filesToTranscribe[i];
            file.ProcessingStatus = AudioProcessingStatus.WaitingToDownloadTranscripts;
            file.CurrentStep = "Waiting for transcription to start...";
            file.ProgressPercentage = 0;

            try
            {
                // Start transcription
                int numSpeakers = session.MicAssignments?.Count ?? 2;
                string transcriptionId = await _gladiaService.StartTranscriptionAsync(
                    file.AudioUrl!, numSpeakers, true, file.FileName);

                file.TranscriptId = transcriptionId;
                file.ProcessingStatus = AudioProcessingStatus.DownloadingTranscripts;
                file.CurrentStep = "Downloading transcripts...";
                file.ProgressPercentage = 50;

                // Wait for completion
                dynamic result = await _gladiaService.WaitForTranscriptionAsync(transcriptionId);

                // Apply speaker mapping
                string? rawTranscript = result.result?.transcription?.full_transcript;
                file.TranscriptText = session.MicAssignments != null && !string.IsNullOrEmpty(rawTranscript)
                    ? _gladiaService.MapSpeakerLabelsToNames(rawTranscript, session.MicAssignments, file.FileName)
                    : rawTranscript;

                file.ProcessingStatus = AudioProcessingStatus.TranscriptsDownloaded;
                file.CurrentStep = "Transcripts downloaded";
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

        // Use the new validation method for synchronized processing
        await ValidateAllFilesTranscribedAndProceed(session, progressCallback);
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
              f.ProcessingStatus == AudioProcessingStatus.FinishedConvertingToMp3))).ToList();

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
            f.ProcessingStatus == AudioProcessingStatus.FinishedConvertingToMp3 ||
            f.ProcessingStatus == AudioProcessingStatus.FailedMp3).ToList();

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
            f.ProcessingStatus == AudioProcessingStatus.FinishedUploadingToGladia &&
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

        foreach (AudioFile audioFile in session.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.TranscriptsDownloaded))
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
            audioFile.ProcessingStatus = AudioProcessingStatus.TranscriptsDownloaded;
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
