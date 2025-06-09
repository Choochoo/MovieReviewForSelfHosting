using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Models;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Components.Partials;

/// <summary>
/// Code-behind file for UnifiedAudioProcessor component that handles audio file processing workflow.
/// </summary>
public partial class UnifiedAudioProcessor : ComponentBase, IDisposable
{
    #region Constants
    private const string MIC_ASSIGNMENTS_KEY = "micAssignments";
    private const string PRESENT_SPEAKERS_KEY = "presentSpeakers";
    #endregion

    #region Private Fields
    private MovieSession? selectedSession;
    private List<MovieEvent>? availableMovies;
    private List<MovieSession> existingSessions = new();
    private Dictionary<int, string> micAssignments = new();
    private HashSet<int> presentSpeakers = new();
    private List<AudioFile> uploadedFiles = new();
    private List<IBrowserFile> selectedFiles = new();
    private Guid? selectedMovieId;
    private string errorMessage = string.Empty;
    private bool isProcessing = false;
    private bool isSaving = false;
    private Timer? transcriptionStatusTimer;
    #endregion

    #region Injected Dependencies
    [Inject] private MovieReviewService MovieReviewService { get; set; } = default!;
    [Inject] private MovieSessionService MovieSessionService { get; set; } = default!;
    [Inject] private GladiaService GladiaService { get; set; } = default!;
    [Inject] private MovieSessionAnalysisService AnalysisService { get; set; } = default!;
    [Inject] private IDatabaseService DatabaseService { get; set; } = default!;
    [Inject] private AudioFileOrganizer AudioOrganizer { get; set; } = default!;
    [Inject] private IWebHostEnvironment WebHostEnvironment { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    #endregion

    #region Properties
    /// <summary>
    /// Gets a value indicating whether a new session can be created based on current state.
    /// </summary>
    private bool CanCreateSession => selectedMovieId.HasValue && selectedFiles.Any() && presentSpeakers.Any();
    #endregion

    #region Lifecycle Methods
    /// <summary>
    /// Initializes the component by loading available movies, existing sessions, and cached assignments.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        availableMovies = await MovieReviewService.GetAllMovieEventsAsync();
        await LoadExistingSessions();
        await LoadCachedAssignments();
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Loads existing movie sessions from the database for display.
    /// </summary>
    private async Task LoadExistingSessions()
    {
        existingSessions = await MovieSessionService.GetAllSessions();
        existingSessions = existingSessions
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Date)
            .Take(20)
            .ToList();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Loads cached microphone assignments and present speakers from browser local storage.
    /// </summary>
    private async Task LoadCachedAssignments()
    {
        try
        {
            string cachedAssignments = await JSRuntime.InvokeAsync<string>("localStorage.getItem", MIC_ASSIGNMENTS_KEY);
            if (!string.IsNullOrEmpty(cachedAssignments))
            {
                micAssignments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(cachedAssignments) ?? new();
            }

            string cachedSpeakers = await JSRuntime.InvokeAsync<string>("localStorage.getItem", PRESENT_SPEAKERS_KEY);
            if (!string.IsNullOrEmpty(cachedSpeakers))
            {
                presentSpeakers = System.Text.Json.JsonSerializer.Deserialize<HashSet<int>>(cachedSpeakers) ?? new();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading cached assignments: {ex}");
            micAssignments = new();
            presentSpeakers = new();
        }
    }

    /// <summary>
    /// Handles movie selection change event.
    /// </summary>
    private async Task OnMovieSelected(ChangeEventArgs e)
    {
        string? movieIdStr = e.Value?.ToString();
        selectedMovieId = !string.IsNullOrEmpty(movieIdStr) && Guid.TryParse(movieIdStr, out Guid movieId) ? movieId : null;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Handles file selection from the input file control.
    /// </summary>
    private async Task LoadFiles(InputFileChangeEventArgs e)
    {
        errorMessage = string.Empty;
        selectedFiles = e.GetMultipleFiles(maximumFileCount: 20).ToList();
        uploadedFiles.Clear();
        
        foreach (IBrowserFile file in selectedFiles)
        {
            if (IsAudioFile(file.Name))
            {
                AudioFile audioFile = new AudioFile
                {
                    FileName = file.Name,
                    FilePath = string.Empty,
                    FileSize = file.Size,
                    ProcessingStatus = AudioProcessingStatus.Pending,
                    CurrentStep = "Ready to upload",
                    ProgressPercentage = 0,
                    CanRetry = true
                };
                uploadedFiles.Add(audioFile);
            }
        }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Creates a new movie session and starts processing workflow.
    /// </summary>
    private async Task CreateSessionAndConvert()
    {
        // Simplified version - full implementation would be longer
        if (!CanCreateSession) return;
        
        isProcessing = true;
        errorMessage = string.Empty;
        
        try
        {
            // Basic session creation logic here
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            errorMessage = $"Error creating session: {ex.Message}";
        }
        finally
        {
            isProcessing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Loads an existing session for editing.
    /// </summary>
    private async Task LoadSession(MovieSession session)
    {
        selectedSession = session;
        micAssignments = new Dictionary<int, string>(session.MicAssignments);
        presentSpeakers = session.MicAssignments.Keys.ToHashSet();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Deletes a session after confirmation.
    /// </summary>
    private async Task DeleteSession(MovieSession session)
    {
        if (await JSRuntime.InvokeAsync<bool>("confirm", $"Are you sure you want to delete the session for '{session.MovieTitle}'?"))
        {
            await DatabaseService.DeleteByIdAsync<MovieSession>(session.Id);
            await LoadExistingSessions();
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Saves the current session.
    /// </summary>
    private async Task SaveSession()
    {
        if (selectedSession == null) return;
        
        isSaving = true;
        try
        {
            await DatabaseService.UpsertAsync(selectedSession);
            BackToSelection();
        }
        finally
        {
            isSaving = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Returns to session selection view.
    /// </summary>
    private void BackToSelection()
    {
        selectedSession = null;
        selectedMovieId = null;
        micAssignments.Clear();
        presentSpeakers.Clear();
        selectedFiles.Clear();
        errorMessage = string.Empty;
    }

    /// <summary>
    /// Removes a file from upload queue.
    /// </summary>
    private void RemoveFile(AudioFile file)
    {
        int fileIndex = uploadedFiles.IndexOf(file);
        uploadedFiles.Remove(file);
        if (fileIndex >= 0 && fileIndex < selectedFiles.Count)
        {
            selectedFiles.RemoveAt(fileIndex);
        }
        StateHasChanged();
    }

    // Placeholder methods for UI functionality
    private async Task StartProcess(AudioFile file, AudioProcessingStatus status) => await Task.CompletedTask;
    private async Task CancelProcess(AudioFile file) => await Task.CompletedTask;
    private async Task StartBulkProcessing(AudioProcessingStatus status) => await Task.CompletedTask;
    private async Task StartFullProcessing(AudioFile file) => await Task.CompletedTask;
    private async Task RerunAIAnalysis(AudioFile file) => await Task.CompletedTask;
    private bool CanStartStep(AudioFile file, AudioProcessingStatus status) => false;

    // Helper methods for UI display
    private bool IsAudioFile(string fileName)
    {
        string[] allowedExtensions = { ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp" };
        return allowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
    }

    private string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return string.Format("{0:n1} {1}", number, suffixes[counter]);
    }

    private string GetRowClass(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Failed => "table-danger",
        AudioProcessingStatus.Complete => "table-success",
        _ when IsInProgress(status) => "table-warning",
        _ => ""
    };

    private string GetStatusBadgeClass(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Failed => "bg-danger",
        AudioProcessingStatus.Complete => "bg-success",
        _ when IsInProgress(status) => "bg-warning",
        _ => "bg-secondary"
    };

    private string GetProgressBarClass(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Failed => "bg-danger",
        AudioProcessingStatus.Complete => "bg-success",
        _ when IsInProgress(status) => "progress-bar-striped progress-bar-animated",
        _ => ""
    };

    private string GetStatusIcon(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Pending => "⏳",
        AudioProcessingStatus.Complete => "✅",
        AudioProcessingStatus.Failed => "❌",
        _ => "❓"
    };

    private string GetStatusText(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Pending => "Ready to process",
        AudioProcessingStatus.Complete => "Complete",
        AudioProcessingStatus.Failed => "Failed",
        _ => $"Status: {status}"
    };

    private bool IsInProgress(AudioProcessingStatus status) => status is 
        AudioProcessingStatus.Uploading or
        AudioProcessingStatus.ConvertingToMp3 or 
        AudioProcessingStatus.UploadingToGladia or 
        AudioProcessingStatus.Transcribing or 
        AudioProcessingStatus.ProcessingWithAI;

    private int GetOverallProgress() => selectedSession?.AudioFiles.Any() == true 
        ? (int)(selectedSession.AudioFiles.Count(f => f.ProcessingStatus == AudioProcessingStatus.Complete) * 100.0 / selectedSession.AudioFiles.Count) 
        : 0;

    private string GetActionButtonText(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Pending => "Start",
        AudioProcessingStatus.Failed => "Retry",
        AudioProcessingStatus.Complete => "Redo",
        _ => "Process"
    };

    private string GetStartButtonTooltip()
    {
        if (isProcessing) return "Processing in progress...";
        if (!selectedMovieId.HasValue) return "Please select a movie";
        if (!selectedFiles.Any()) return "Please upload audio files";
        if (!presentSpeakers.Any()) return "Please assign speakers";
        return "Start processing audio files";
    }

    private string GetSessionState() => $"Movie: {selectedMovieId.HasValue}, Files: {selectedFiles.Count}, Speakers: {presentSpeakers.Count}";

    private string GetSessionStatusBadgeClass(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Pending => "bg-secondary",
        ProcessingStatus.Complete => "bg-success",
        ProcessingStatus.Failed => "bg-danger",
        _ => "bg-secondary"
    };
    #endregion

    #region IDisposable
    /// <summary>
    /// Releases resources used by the component.
    /// </summary>
    public void Dispose()
    {
        transcriptionStatusTimer?.Dispose();
    }
    #endregion
}