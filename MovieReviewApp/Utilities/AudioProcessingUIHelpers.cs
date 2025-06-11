using MovieReviewApp.Models;

namespace MovieReviewApp.Utilities;

/// <summary>
/// Shared UI helper methods for audio processing components.
/// Contains common styling, progress calculation, and status display logic.
/// </summary>
public static class AudioProcessingUIHelpers
{
    /// <summary>
    /// Gets CSS class for table rows based on processing status.
    /// </summary>
    public static string GetRowClass(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Failed => "table-danger",
        AudioProcessingStatus.Complete => "table-success",
        _ when IsInProgress(status) => "table-warning",
        _ => ""
    };

    /// <summary>
    /// Gets CSS class for status badges.
    /// </summary>
    public static string GetStatusBadgeClass(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Failed => "bg-danger",
        AudioProcessingStatus.Complete => "bg-success",
        _ when IsInProgress(status) => "bg-warning",
        _ => "bg-secondary"
    };

    /// <summary>
    /// Gets CSS class for progress bars.
    /// </summary>
    public static string GetProgressBarClass(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Failed => "bg-danger",
        AudioProcessingStatus.Complete => "bg-success",
        _ when IsInProgress(status) => "progress-bar-striped progress-bar-animated",
        _ => ""
    };

    /// <summary>
    /// Gets icon for processing status.
    /// </summary>
    public static string GetStatusIcon(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Pending => "⏳",
        AudioProcessingStatus.Uploading => "📤",
        AudioProcessingStatus.ConvertingToMp3 => "🔄",
        AudioProcessingStatus.FinishedConvertingToMp3 => "✅",
        AudioProcessingStatus.FailedMp3 => "❌",
        AudioProcessingStatus.UploadingToGladia => "☁️",
        AudioProcessingStatus.FinishedUploadingToGladia => "✅",
        AudioProcessingStatus.WaitingToDownloadTranscripts => "⏳",
        AudioProcessingStatus.DownloadingTranscripts => "⬇️",
        AudioProcessingStatus.TranscriptsDownloaded => "📝",
        AudioProcessingStatus.WaitingForOtherFiles => "⏳",
        AudioProcessingStatus.ProcessingTranscriptions => "📋",
        AudioProcessingStatus.SendingToOpenAI => "📤",
        AudioProcessingStatus.ProcessingWithAI => "🤖",
        AudioProcessingStatus.Complete => "✅",
        AudioProcessingStatus.Failed => "❌",
        _ => "❓"
    };

    /// <summary>
    /// Gets human-readable status text.
    /// </summary>
    public static string GetStatusText(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Pending => "Waiting to start",
        AudioProcessingStatus.Uploading => "Uploading...",
        AudioProcessingStatus.ConvertingToMp3 => "Converting to MP3...",
        AudioProcessingStatus.FinishedConvertingToMp3 => "Converting complete, uploading...",
        AudioProcessingStatus.FailedMp3 => "MP3 conversion failed",
        AudioProcessingStatus.UploadingToGladia => "Uploading to Gladia...",
        AudioProcessingStatus.FinishedUploadingToGladia => "Upload complete, transcribing...",
        AudioProcessingStatus.WaitingToDownloadTranscripts => "Starting transcription...",
        AudioProcessingStatus.DownloadingTranscripts => "Downloading transcripts...",
        AudioProcessingStatus.TranscriptsDownloaded => "Transcripts ready",
        AudioProcessingStatus.WaitingForOtherFiles => "Waiting for other files",
        AudioProcessingStatus.ProcessingTranscriptions => "Processing transcriptions...",
        AudioProcessingStatus.SendingToOpenAI => "Sending to OpenAI...",
        AudioProcessingStatus.ProcessingWithAI => "Processing with AI...",
        AudioProcessingStatus.Complete => "Complete",
        AudioProcessingStatus.Failed => "Processing failed",
        _ => $"Status: {status}"
    };

    /// <summary>
    /// Checks if a status represents an in-progress operation.
    /// </summary>
    public static bool IsInProgress(AudioProcessingStatus status) => status is
        AudioProcessingStatus.Uploading or
        AudioProcessingStatus.ConvertingToMp3 or
        AudioProcessingStatus.UploadingToGladia or
        AudioProcessingStatus.DownloadingTranscripts or
        AudioProcessingStatus.ProcessingTranscriptions or
        AudioProcessingStatus.SendingToOpenAI or
        AudioProcessingStatus.ProcessingWithAI;

    /// <summary>
    /// Gets action button text based on status.
    /// </summary>
    public static string GetActionButtonText(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Pending => "Start",
        AudioProcessingStatus.Failed => "Retry",
        AudioProcessingStatus.Complete => "Redo",
        _ => "Process"
    };

    /// <summary>
    /// Gets CSS class for session status badges.
    /// </summary>
    public static string GetSessionStatusBadgeClass(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Pending => "bg-secondary",
        ProcessingStatus.Complete => "bg-success",
        ProcessingStatus.Failed => "bg-danger",
        _ => "bg-secondary"
    };

    /// <summary>
    /// Calculates progress value for a specific status.
    /// </summary>
    public static double GetFileProgressValue(AudioProcessingStatus status)
    {
        return status switch
        {
            AudioProcessingStatus.Pending => 0,
            AudioProcessingStatus.Uploading => 5,
            AudioProcessingStatus.ConvertingToMp3 => 10,
            AudioProcessingStatus.FinishedConvertingToMp3 => 20,
            AudioProcessingStatus.UploadingToGladia => 25,
            AudioProcessingStatus.FinishedUploadingToGladia => 35,
            AudioProcessingStatus.WaitingToDownloadTranscripts => 40,
            AudioProcessingStatus.DownloadingTranscripts => 45,
            AudioProcessingStatus.TranscriptsDownloaded => 55,
            AudioProcessingStatus.WaitingForOtherFiles => 70,
            AudioProcessingStatus.ProcessingTranscriptions => 80,
            AudioProcessingStatus.SendingToOpenAI => 85,
            AudioProcessingStatus.ProcessingWithAI => 90,
            AudioProcessingStatus.Complete => 100,
            AudioProcessingStatus.Failed => 0,
            AudioProcessingStatus.FailedMp3 => 0,
            _ => 0
        };
    }

    /// <summary>
    /// Calculates overall progress for a session.
    /// </summary>
    public static int GetOverallProgress(MovieSession? session)
    {
        if (session?.AudioFiles.Any() != true)
            return 0;

        List<AudioFile> audioFiles = session.AudioFiles;
        int totalFiles = audioFiles.Count;
        double totalProgress = 0.0;

        foreach (AudioFile file in audioFiles)
        {
            double fileProgress = GetFileProgressValue(file.ProcessingStatus);

            if (IsInProgress(file.ProcessingStatus) && file.ProgressPercentage > 0)
            {
                double statusProgress = fileProgress;
                double nextStatusProgress = GetNextStatusProgressValue(file.ProcessingStatus);
                double progressRange = nextStatusProgress - statusProgress;
                double blendedProgress = statusProgress + (progressRange * file.ProgressPercentage / 100.0);
                totalProgress += blendedProgress;
            }
            else
            {
                totalProgress += fileProgress;
            }
        }

        return (int)(totalProgress / totalFiles);
    }

    /// <summary>
    /// Gets progress value for the next status in sequence.
    /// </summary>
    public static double GetNextStatusProgressValue(AudioProcessingStatus status)
    {
        return status switch
        {
            AudioProcessingStatus.Uploading => GetFileProgressValue(AudioProcessingStatus.ConvertingToMp3),
            AudioProcessingStatus.ConvertingToMp3 => GetFileProgressValue(AudioProcessingStatus.FinishedConvertingToMp3),
            AudioProcessingStatus.UploadingToGladia => GetFileProgressValue(AudioProcessingStatus.FinishedUploadingToGladia),
            AudioProcessingStatus.DownloadingTranscripts => GetFileProgressValue(AudioProcessingStatus.TranscriptsDownloaded),
            AudioProcessingStatus.ProcessingTranscriptions => GetFileProgressValue(AudioProcessingStatus.SendingToOpenAI),
            AudioProcessingStatus.SendingToOpenAI => GetFileProgressValue(AudioProcessingStatus.ProcessingWithAI),
            AudioProcessingStatus.ProcessingWithAI => GetFileProgressValue(AudioProcessingStatus.Complete),
            _ => GetFileProgressValue(status)
        };
    }

    /// <summary>
    /// Determines if a file can start processing from a specific status.
    /// </summary>
    public static bool CanStartStep(AudioFile file, AudioProcessingStatus targetStatus)
    {
        return targetStatus switch
        {
            AudioProcessingStatus.ConvertingToMp3 => file.ProcessingStatus <= AudioProcessingStatus.ConvertingToMp3,
            AudioProcessingStatus.UploadingToGladia => file.ProcessingStatus <= AudioProcessingStatus.UploadingToGladia,
            AudioProcessingStatus.DownloadingTranscripts => file.ProcessingStatus <= AudioProcessingStatus.DownloadingTranscripts,
            AudioProcessingStatus.ProcessingWithAI => file.ProcessingStatus <= AudioProcessingStatus.ProcessingWithAI,
            _ => false
        };
    }
}