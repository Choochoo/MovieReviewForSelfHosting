using MovieReviewApp.Application.Services.Analysis;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services.Processing;

/// <summary>
/// A clean, recursive state machine for audio processing that handles individual audio files
/// through a series of well-defined states until they reach completion.
/// 
/// Core Philosophy:
/// - Each state knows only how to do its work and what the next state should be
/// - After completing work, the function updates status and recursively calls itself
/// - This continues until reaching a terminal state (Complete or Failed)
/// - Each audio file processes independently and asynchronously
/// </summary>
public class AudioProcessingStateMachine
{
    private readonly GladiaService _gladiaService;
    private readonly SpeakerAttributionFixService _speakerAttributionService;
    private readonly ILogger<AudioProcessingStateMachine> _logger;

    public AudioProcessingStateMachine(
        GladiaService gladiaService,
        SpeakerAttributionFixService speakerAttributionService,
        ILogger<AudioProcessingStateMachine> logger)
    {
        _gladiaService = gladiaService;
        _speakerAttributionService = speakerAttributionService;
        _logger = logger;
    }

    /// <summary>
    /// Processes a single audio file through all states recursively until completion.
    /// This is the main entry point that starts the recursive state machine.
    /// </summary>
    public async Task ProcessAudioFileAsync(AudioFile audioFile, MovieSession session)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);
        _logger.LogInformation("Starting recursive state machine processing for {FileName} at state {CurrentState}",
            fileName, audioFile.ProcessingStatus);

        try
        {
            await ProcessCurrentStateAsync(audioFile, session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State machine processing failed for {FileName} at state {CurrentState}",
                fileName, audioFile.ProcessingStatus);

            audioFile.ProcessingStatus = AudioProcessingStatus.Failed;
            audioFile.ConversionError = ex.Message;
            audioFile.CurrentStep = $"Failed: {ex.Message}";
            audioFile.CanRetry = true;
            audioFile.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The recursive core of the state machine. Processes the current state and recursively
    /// calls itself until reaching a terminal state (Complete or Failed).
    /// </summary>
    private async Task ProcessCurrentStateAsync(AudioFile audioFile, MovieSession session)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);
        AudioProcessingStatus currentState = audioFile.ProcessingStatus;

        _logger.LogDebug("Processing state {CurrentState} for {FileName}", currentState, fileName);

        // Terminal states - stop recursion
        if (currentState == AudioProcessingStatus.Complete || currentState == AudioProcessingStatus.Failed)
        {
            _logger.LogInformation("Reached terminal state {TerminalState} for {FileName}", currentState, fileName);
            return;
        }

        // Process current state
        switch (currentState)
        {
            // Phase 1: Individual File Processing (Parallel/Async)
            case AudioProcessingStatus.Pending:
                await ProcessPendingState(audioFile, session);
                break;
            case AudioProcessingStatus.Uploading:
                await ProcessUploadingState(audioFile, session);
                break;
            case AudioProcessingStatus.ConvertingToMp3:
                await ProcessConvertingToMp3State(audioFile, session);
                break;
            case AudioProcessingStatus.FinishedConvertingToMp3:
                ProcessFinishedConvertingToMp3State(audioFile);
                break;
            case AudioProcessingStatus.UploadingToGladia:
                await ProcessUploadingToGladiaState(audioFile, session);
                break;
            case AudioProcessingStatus.FinishedUploadingToGladia:
                await ProcessDownloadingTranscriptsState(audioFile, session);
                break;
            case AudioProcessingStatus.DownloadingTranscripts:
                await ProcessDownloadingTranscriptsState(audioFile, session);
                break;
            case AudioProcessingStatus.TranscriptsDownloaded:
                ProcessTranscriptsDownloadedState(audioFile);
                break;

            // Phase 2: Synchronization Point
            case AudioProcessingStatus.WaitingForOtherFiles:
                // This is the synchronization point - files wait here until all reach this state
                _logger.LogDebug("File {FileName} is waiting for other files - no action needed", fileName);
                return;

            // Phase 3: Collective Processing (Sequential)
            case AudioProcessingStatus.ProcessingTranscriptions:
                // This state is handled at the session level - all files must be in this state
                await ProcessProcessingTranscriptionsState(audioFile, session);
                break;
            case AudioProcessingStatus.SendingToOpenAI:
                // This state is handled at the session level
                _logger.LogDebug("File {FileName} reached OpenAI processing - handled at session level", fileName);
                return;
            case AudioProcessingStatus.ProcessingWithAI:
                // This state is handled at the session level
                _logger.LogDebug("File {FileName} in AI processing - handled at session level", fileName);
                return;
            case AudioProcessingStatus.ReadyToProcessAIResponse:
                ProcessReadyToProcessAIResponseState(audioFile);
                break;
            case AudioProcessingStatus.ProcessingAIResponse:
                ProcessProcessingAIResponseState(audioFile);
                break;
            default:
                throw new InvalidOperationException($"Unhandled state: {currentState}");
        }

        // Recursively process next state
        await ProcessCurrentStateAsync(audioFile, session);
    }

    #region State Processing Methods

    /// <summary>
    /// Handles the Pending state - determines if file needs conversion or can go directly to upload.
    /// </summary>
    private async Task ProcessPendingState(AudioFile audioFile, MovieSession session)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);
        string extension = Path.GetExtension(audioFile.FilePath).ToLowerInvariant();

        audioFile.CurrentStep = "Analyzing file type...";
        audioFile.LastUpdated = DateTime.UtcNow;

        if (extension == ".wav")
        {
            _logger.LogDebug("File {FileName} is WAV - needs conversion to MP3", fileName);
            audioFile.ProcessingStatus = AudioProcessingStatus.ConvertingToMp3;
        }
        else if (extension == ".mp3")
        {
            _logger.LogDebug("File {FileName} is already MP3 - can upload directly", fileName);
            audioFile.ProcessingStatus = AudioProcessingStatus.UploadingToGladia;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported file type: {extension}");
        }
    }

    /// <summary>
    /// Handles the Uploading state - for files that were uploaded via web interface.
    /// </summary>
    private async Task ProcessUploadingState(AudioFile audioFile, MovieSession session)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);
        string extension = Path.GetExtension(audioFile.FilePath).ToLowerInvariant();

        audioFile.CurrentStep = "Uploading to Gladia...";
        audioFile.ProgressPercentage = 0;
        audioFile.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation("Uploading {FileName} to Gladia", fileName);

        try
        {
            string audioUrl;
            if (extension == ".mp3")
            {
                // File is already MP3, use UploadSingleFileToGladiaAsync directly
                audioUrl = await _gladiaService.UploadSingleFileToGladiaAsync(audioFile.FilePath);
            }
            else
            {
                // File is not MP3, use UploadFileAsync to handle conversion if needed
                audioUrl = await _gladiaService.UploadFileAsync(audioFile.FilePath, audioFile);
            }

            audioFile.AudioUrl = audioUrl;
            audioFile.UploadedAt = DateTime.UtcNow;

            // Update progress based on the number of files uploaded
            int totalFiles = session.AudioFiles.Count;
            int uploadedFiles = session.AudioFiles.Count(f => f.ProcessingStatus == AudioProcessingStatus.FinishedUploadingToGladia);
            audioFile.ProgressPercentage = (int)((double)uploadedFiles / totalFiles * 100);

            audioFile.ProcessingStatus = AudioProcessingStatus.FinishedUploadingToGladia;
            audioFile.CurrentStep = "Upload to Gladia complete";
            audioFile.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Successfully uploaded {FileName} to Gladia", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload {FileName} to Gladia", fileName);
            audioFile.ProcessingStatus = AudioProcessingStatus.Failed;
            audioFile.ConversionError = ex.Message;
            audioFile.CurrentStep = "Upload to Gladia failed";
            throw;
        }
    }

    /// <summary>
    /// Handles the ConvertingToMp3 state - converts WAV files to MP3.
    /// </summary>
    private async Task ProcessConvertingToMp3State(AudioFile audioFile, MovieSession session)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);

        audioFile.CurrentStep = "Converting WAV to MP3...";
        audioFile.ProgressPercentage = 10;
        audioFile.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation("Converting {FileName} from WAV to MP3", fileName);

        // Convert single file
        string? mp3FileName = Path.ChangeExtension(Path.GetFileName(audioFile.FilePath), ".mp3");
        string mp3Path = Path.Combine(session.FolderPath, mp3FileName);

        await _gladiaService.ConvertToMp3Async(audioFile.FilePath, mp3Path, (step, current, total) =>
        {
            int progress = (int)((double)current / total * 90) + 10; // 10% to 100%
            audioFile.ProgressPercentage = progress;
            audioFile.CurrentStep = $"Converting: {step}";
            audioFile.LastUpdated = DateTime.UtcNow;
        });

        // Update audio file with MP3 details
        audioFile.ProcessingStatus = AudioProcessingStatus.FinishedConvertingToMp3;
        audioFile.ConvertedAt = DateTime.UtcNow;

        // Delete the original WAV file
        string originalWavPath = audioFile.FilePath;
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

        audioFile.CurrentStep = "Conversion complete";
        audioFile.ProgressPercentage = 100;
        audioFile.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation("Converted {FileName} to MP3: {Mp3Path}",
            Path.GetFileName(audioFile.FilePath), mp3Path);
    }

    /// <summary>
    /// Handles the FinishedConvertingToMp3 state - transitions to upload.
    /// </summary>
    private void ProcessFinishedConvertingToMp3State(AudioFile audioFile)
    {
        audioFile.ProcessingStatus = AudioProcessingStatus.UploadingToGladia;
        audioFile.CurrentStep = "Ready for upload";
        audioFile.ProgressPercentage = 0; // Reset for upload phase
        audioFile.LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Handles the UploadingToGladia state - uploads files to Gladia service.
    /// </summary>
    private async Task ProcessUploadingToGladiaState(AudioFile audioFile, MovieSession session)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);

        audioFile.CurrentStep = "Uploading to Gladia...";
        audioFile.ProgressPercentage = 10;
        audioFile.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation("Uploading {FileName} to Gladia", fileName);

        try
        {
            // Upload single file to Gladia
            string audioUrl = await _gladiaService.UploadFileAsync(audioFile.FilePath, audioFile);
            audioFile.AudioUrl = audioUrl;
            audioFile.UploadedAt = DateTime.UtcNow;

            // Update progress based on the number of files uploaded
            int totalFiles = session.AudioFiles.Count;
            int uploadedFiles = session.AudioFiles.Count(f => f.ProcessingStatus == AudioProcessingStatus.FinishedUploadingToGladia);
            audioFile.ProgressPercentage = (int)((double)uploadedFiles / totalFiles * 100);

            audioFile.ProcessingStatus = AudioProcessingStatus.FinishedUploadingToGladia;
            audioFile.CurrentStep = "Upload complete";
            audioFile.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Successfully uploaded {FileName} to Gladia", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload {FileName} to Gladia", fileName);
            audioFile.ProcessingStatus = AudioProcessingStatus.Failed;
            audioFile.ConversionError = ex.Message;
            audioFile.CurrentStep = "Upload failed";
            throw;
        }
    }

    /// <summary>
    /// Handles the FinishedUploadingToGladia state - transitions to transcription.
    /// </summary>
    private async Task ProcessDownloadingTranscriptsState(AudioFile audioFile, MovieSession session)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);
        
        if (string.IsNullOrEmpty(audioFile.AudioUrl))
        {
            throw new InvalidOperationException($"File {fileName} has no AudioUrl - cannot start transcription");
        }
        
        audioFile.CurrentStep = "Processing transcription...";
        audioFile.ProgressPercentage = 25;
        audioFile.LastUpdated = DateTime.UtcNow;
        
        _logger.LogInformation("Starting transcription for {FileName}", fileName);

        // Use ProcessMultipleFilesAsync to handle the transcription
        List<TranscriptionResult> results = await _gladiaService.ProcessMultipleFilesAsync(
            new List<AudioFile> { audioFile },
            session.MicAssignments,
            (step, current, total) =>
            {
                int progress = (int)((double)current / total * 90) + 10; // 10% to 100%
                audioFile.ProgressPercentage = progress;
                audioFile.CurrentStep = $"Processing: {step}";
                audioFile.LastUpdated = DateTime.UtcNow;
            });

        if (results.Any())
        {
            TranscriptionResult result = results.First();
            
            // Check for errors
            if (result.error != null)
            {
                throw new InvalidOperationException($"Transcription failed for {fileName}: {result.error.message}");
            }

            // Check status
            if (result.status != "success")
            {
                throw new InvalidOperationException($"Transcription failed for {fileName}: Status {result.status}");
            }

            // Update file with transcription results
            audioFile.TranscriptId = result.id;
            audioFile.TranscriptText = result.result?.transcription?.full_transcript;
            audioFile.JsonFilePath = result.source_file_path;

            audioFile.ProcessingStatus = AudioProcessingStatus.TranscriptsDownloaded;
            audioFile.CurrentStep = "Transcription complete";
            audioFile.ProgressPercentage = 100;
            audioFile.ProcessedAt = DateTime.UtcNow;
            audioFile.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Transcription completed for {FileName}", fileName);
        }
        else
        {
            throw new InvalidOperationException($"No transcription results returned for {fileName}");
        }
    }

    /// <summary>
    /// Handles the TranscriptsDownloaded state - transitions to waiting for other files.
    /// </summary>
    private void ProcessTranscriptsDownloadedState(AudioFile audioFile)
    {
        // Move to waiting state - this is the synchronization point
        audioFile.ProcessingStatus = AudioProcessingStatus.WaitingForOtherFiles;
        audioFile.CurrentStep = "Waiting for other files to complete";
        audioFile.LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Handles the ProcessingTranscriptions state - runs speaker attribution fix.
    /// </summary>
    private async Task ProcessProcessingTranscriptionsState(AudioFile audioFile, MovieSession session)
    {
        // This state is handled at the session level
        // The orchestrator will ensure all files are in this state before proceeding
        audioFile.CurrentStep = "Processing transcriptions with speaker attribution...";
        audioFile.ProgressPercentage = 50;
        audioFile.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation("Running speaker attribution fix for session {SessionId}", session.Id);

        var result = await _speakerAttributionService.FixSpeakerAttributionForSession(session);

        if (result.Success)
        {
            _logger.LogInformation("Speaker attribution completed successfully for session {SessionId}", session.Id);
            audioFile.ProcessingStatus = AudioProcessingStatus.SendingToOpenAI;
            audioFile.CurrentStep = "Ready for AI analysis";
            audioFile.ProgressPercentage = 100;
        }
        else
        {
            _logger.LogError("Speaker attribution failed for session {SessionId}: {Error}", session.Id, result.ErrorMessage);
            audioFile.ProcessingStatus = AudioProcessingStatus.Failed;
            audioFile.ConversionError = result.ErrorMessage ?? "Speaker attribution failed";
            audioFile.CurrentStep = "Speaker attribution failed";
        }

        audioFile.LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Handles the ReadyToProcessAIResponse state - transitions to processing AI response.
    /// </summary>
    private void ProcessReadyToProcessAIResponseState(AudioFile audioFile)
    {
        audioFile.ProcessingStatus = AudioProcessingStatus.ProcessingAIResponse;
        audioFile.CurrentStep = "Processing AI response...";
        audioFile.LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Handles the ProcessingAIResponse state - completes the processing.
    /// </summary>
    private void ProcessProcessingAIResponseState(AudioFile audioFile)
    {
        audioFile.ProcessingStatus = AudioProcessingStatus.Complete;
        audioFile.CurrentStep = "Processing complete";
        audioFile.ProgressPercentage = 100;
        audioFile.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation("Audio file {FileName} processing completed successfully",
            Path.GetFileName(audioFile.FilePath));
    }

    #endregion

    /// <summary>
    /// Checks if an audio file can be retried from its current state.
    /// </summary>
    public bool CanRetryFromCurrentState(AudioFile audioFile)
    {
        return audioFile.ProcessingStatus switch
        {
            AudioProcessingStatus.Failed => true,
            AudioProcessingStatus.FailedMp3 => true,
            AudioProcessingStatus.Pending => true,
            AudioProcessingStatus.ConvertingToMp3 => true,
            AudioProcessingStatus.UploadingToGladia => true,
            AudioProcessingStatus.DownloadingTranscripts => true,
            _ => false
        };
    }

    /// <summary>
    /// Resets an audio file to a specific state for retry purposes.
    /// </summary>
    public void ResetToState(AudioFile audioFile, AudioProcessingStatus targetState)
    {
        string fileName = Path.GetFileName(audioFile.FilePath);
        _logger.LogInformation("Resetting {FileName} from {CurrentState} to {TargetState}",
            fileName, audioFile.ProcessingStatus, targetState);

        audioFile.ProcessingStatus = targetState;
        audioFile.ConversionError = null;
        audioFile.ProgressPercentage = 0;
        audioFile.CanRetry = false;
        audioFile.LastUpdated = DateTime.UtcNow;

        // Set appropriate current step based on target state
        audioFile.CurrentStep = targetState switch
        {
            AudioProcessingStatus.Pending => "Ready to process",
            AudioProcessingStatus.ConvertingToMp3 => "Ready for conversion",
            AudioProcessingStatus.UploadingToGladia => "Ready for upload",
            AudioProcessingStatus.DownloadingTranscripts => "Ready for transcription",
            _ => "Ready to resume"
        };
    }
}
