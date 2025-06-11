using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Application.Services.Analysis;
using MovieReviewApp.Application.Services.Processing;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;

namespace MovieReviewApp.Components.Partials;

/// <summary>
/// Code-behind file for UnifiedAudioProcessor component that handles audio file processing workflow.
/// Refactored to use shared utilities and eliminate code duplication.
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
    private Timer? progressTimer;
    private PurgeResult? purgeResult;
    private PurgeStep purgeStep = PurgeStep.Ready;
    private bool isPurging = false;
    private int purgeProgress = 0;
    private int transcriptionCount = 0;
    private bool deleteConfirmation = false;
    #endregion

    #region Injected Dependencies
    [Inject] private MovieEventService MovieEventService { get; set; } = default!;
    [Inject] private MovieSessionService MovieSessionService { get; set; } = default!;
    [Inject] private AnalysisService AnalysisService { get; set; } = default!;
    [Inject] private FileProcessingService FileProcessingService { get; set; } = default!;
    [Inject] private AudioProcessingStateMachine AudioProcessingStateMachine { get; set; } = default!;
    [Inject] private FileUploadService FileUploadService { get; set; } = default!;
    [Inject] private IWebHostEnvironment WebHostEnvironment { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<UnifiedAudioProcessor> _logger { get; set; } = default!;
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
        availableMovies = await MovieEventService.GetAllAsync();
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
        existingSessions = await MovieSessionService.GetAllAsync();
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
            _logger.LogError(ex, "Error loading cached assignments");
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
            if (AudioFileHelpers.IsAudioFile(file.Name))
            {
                AudioFile audioFile = FileUploadService.CreateAudioFileFromBrowserFile(file, micAssignments);
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
        if (!CanCreateSession) return;

        isProcessing = true;
        errorMessage = string.Empty;

        try
        {
            // Get selected movie details
            if (!selectedMovieId.HasValue)
            {
                throw new InvalidOperationException("No movie selected");
            }

            MovieEvent selectedMovie = availableMovies.First(m => m.Id == selectedMovieId.Value);

            // Create session folder for uploads
            string sessionFolderPath = FileUploadService.CreateSessionFolderPath(selectedMovie.Movie, selectedMovie.StartDate);

            // Upload files with progress tracking
            _ = await FileUploadService.UploadFilesWithProgressAsync(
                selectedFiles,
                sessionFolderPath,
                micAssignments,
                (fileName, progress) => InvokeAsync(() =>
                {
                    AudioFile? file = uploadedFiles.FirstOrDefault(f => f.FileName == fileName);
                    if (file != null)
                    {
                        file.ProgressPercentage = progress;
                        StateHasChanged();
                    }
                })
            );

            // Create and save session
            MovieSession preparedSession = await FileProcessingService.PrepareSessionFromFolderAsync(sessionFolderPath, micAssignments);
            preparedSession.Date = selectedMovie.StartDate;
            preparedSession.MovieTitle = selectedMovie.Movie;
            preparedSession.MicAssignments = micAssignments;
            preparedSession.Status = ProcessingStatus.Pending;

            selectedSession = await MovieSessionService.UpsertAsync(preparedSession);

            await InvokeAsync(StateHasChanged);

            // Start processing automatically
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500); // Let UI update
                    await StartBulkProcessing(AudioProcessingStatus.ConvertingToMp3);
                }
                catch (Exception ex)
                {
                    await InvokeAsync(() =>
                    {
                        errorMessage = $"Auto-processing failed: {ex.Message}";
                        StateHasChanged();
                    });
                }
            });
        }
        catch (Exception ex)
        {
            errorMessage = $"Error creating session: {ex.Message}";
            _logger.LogError(ex, "Failed to create session");

            // Reset file statuses on error
            foreach (AudioFile file in uploadedFiles)
            {
                file.ProcessingStatus = AudioProcessingStatus.Pending;
                file.CurrentStep = "Upload failed";
                file.ProgressPercentage = 0;
            }
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
            _ = await MovieSessionService.DeleteAsync(session.Id);
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
            _ = await MovieSessionService.UpsertAsync(selectedSession);
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
        _ = uploadedFiles.Remove(file);
        if (fileIndex >= 0 && fileIndex < selectedFiles.Count)
        {
            selectedFiles.RemoveAt(fileIndex);
        }
        StateHasChanged();
    }

    /// <summary>
    /// Starts bulk processing of audio files using service orchestration.
    /// </summary>
    private async Task StartBulkProcessing(AudioProcessingStatus status)
    {
        if (selectedSession == null)
        {
            errorMessage = "No session selected for processing";
            return;
        }

        isProcessing = true;
        errorMessage = string.Empty;

        try
        {
            await InvokeAsync(StateHasChanged);

            // Reset files to the specified state
            foreach (AudioFile file in selectedSession.AudioFiles)
            {
                if (file.ProcessingStatus < status)
                {
                    file.ProcessingStatus = status;
                    file.CurrentStep = $"Starting from {status}";
                    file.ProgressPercentage = 0;
                    file.LastUpdated = DateTime.UtcNow;
                }
            }

            // Save the session with updated file states
            _ = await MovieSessionService.CreateAsync(selectedSession);

            // Use the file processing service for all processing
            await FileProcessingService.ProcessSessionFilesAsync(selectedSession, (message, progress) =>
            {
                _ = InvokeAsync(() =>
                {
                    StateHasChanged();
                    return Task.CompletedTask;
                });
            });

            StartProgressPolling();
        }
        catch (Exception ex)
        {
            errorMessage = $"Processing failed: {ex.Message}";
            _logger.LogError(ex, "Failed to process session {SessionId}", selectedSession.Id);
        }
        finally
        {
            isProcessing = false;
            progressTimer?.Dispose();
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Shows the purge modal for Gladia data cleanup.
    /// </summary>
    private void ShowPurgeModal()
    {
        purgeStep = PurgeStep.Ready;
        purgeResult = null;
        StateHasChanged();
    }

    /// <summary>
    /// Starts processing a single audio file from a specific status using the state machine.
    /// </summary>
    private async Task StartProcess(AudioFile file, AudioProcessingStatus fromStatus)
    {
        if (selectedSession == null) return;

        try
        {
            _logger.LogInformation("Starting process for {FileName} from status {Status}", file.FileName, fromStatus);

            // Reset the file to the target status
            AudioProcessingStateMachine.ResetToState(file, fromStatus);

            // Save the session with updated file status
            _ = await MovieSessionService.CreateAsync(selectedSession);

            // Start the state machine processing for this file
            await AudioProcessingStateMachine.ProcessAudioFileAsync(file, selectedSession);

            // Refresh the session to get updated status
            await RefreshSessionStatus();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to start processing {file.FileName}: {ex.Message}";
            _logger.LogError(ex, "Failed to start processing for file {FileName}", file.FileName);
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Cancels processing for a specific file.
    /// </summary>
    private async Task CancelProcess(AudioFile file)
    {
        try
        {
            file.ProcessingStatus = AudioProcessingStatus.Failed;
            file.CurrentStep = "Cancelled by user";
            file.ConversionError = "Processing was cancelled";
            file.LastUpdated = DateTime.UtcNow;

            if (selectedSession != null)
            {
                _ = await MovieSessionService.UpsertAsync(selectedSession);
            }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to cancel processing: {ex.Message}";
            _logger.LogError(ex, "Failed to cancel processing for file {FileName}", file.FileName);
        }
    }

    /// <summary>
    /// Processes AI response locally for the current session.
    /// </summary>
    private async Task ProcessAIResponseLocally()
    {
        if (selectedSession == null) return;

        try
        {
            isProcessing = true;
            await InvokeAsync(StateHasChanged);

            _logger.LogInformation("Processing AI response locally for session {SessionId}", selectedSession.Id);

            bool success = await AnalysisService.ProcessAIResponseAsync(selectedSession.Id);

            if (success)
            {
                selectedSession.Status = ProcessingStatus.Complete;
                _ = await MovieSessionService.UpsertAsync(selectedSession);
            }
            else
            {
                errorMessage = "Failed to process AI response locally";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to process AI response: {ex.Message}";
            _logger.LogError(ex, "Failed to process AI response for session {SessionId}", selectedSession?.Id);
        }
        finally
        {
            isProcessing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ClosePurgeModal()
    {
        purgeResult = null;
        purgeStep = PurgeStep.Ready;
        StateHasChanged();
    }

    private void StartProgressPolling()
    {
        progressTimer?.Dispose();
        progressTimer = new Timer(async _ =>
        {
            if (isProcessing && selectedSession != null)
            {
                await RefreshSessionStatus();
                await InvokeAsync(StateHasChanged);
            }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    private async Task RefreshSessionStatus()
    {
        if (selectedSession != null)
        {
            MovieSession? latest = await MovieSessionService.GetByIdAsync(selectedSession.Id);
            if (latest != null)
                selectedSession = latest;
        }
    }

    // Delegate all UI helper methods to the shared utilities
    private bool CanStartStep(AudioFile file, AudioProcessingStatus targetStatus) =>
        AudioProcessingUIHelpers.CanStartStep(file, targetStatus);

    private string GetRowClass(AudioProcessingStatus status) =>
        AudioProcessingUIHelpers.GetRowClass(status);

    private string GetStatusBadgeClass(AudioProcessingStatus status) =>
        AudioProcessingUIHelpers.GetStatusBadgeClass(status);

    private string GetProgressBarClass(AudioProcessingStatus status) =>
        AudioProcessingUIHelpers.GetProgressBarClass(status);

    private string GetStatusIcon(AudioProcessingStatus status) =>
        AudioProcessingUIHelpers.GetStatusIcon(status);

    private string GetStatusText(AudioProcessingStatus status) =>
        AudioProcessingUIHelpers.GetStatusText(status);

    private bool IsInProgress(AudioProcessingStatus status) =>
        AudioProcessingUIHelpers.IsInProgress(status);

    private int GetOverallProgress() =>
        AudioProcessingUIHelpers.GetOverallProgress(selectedSession);

    private string GetActionButtonText(AudioProcessingStatus status) =>
        AudioProcessingUIHelpers.GetActionButtonText(status);

    private string GetSessionStatusBadgeClass(ProcessingStatus status) =>
        AudioProcessingUIHelpers.GetSessionStatusBadgeClass(status);

    private string FormatBytes(long bytes) =>
        AudioFileHelpers.FormatBytes(bytes);

    private string GetStartButtonTooltip()
    {
        if (isProcessing) return "Processing in progress...";
        if (!selectedMovieId.HasValue) return "Please select a movie";
        if (!selectedFiles.Any()) return "Please upload audio files";
        if (!presentSpeakers.Any()) return "Please assign speakers";
        return "Start processing audio files";
    }

    private string GetSessionState() => $"Movie: {selectedMovieId.HasValue}, Files: {selectedFiles.Count}, Speakers: {presentSpeakers.Count}";
    #endregion

    #region Enums and Classes
    public enum PurgeStep
    {
        Ready,
        InProgress,
        Purging,
        Complete
    }

    public class PurgeResult
    {
        public int TotalDeleted { get; set; }
        public int TotalFailed { get; set; }
        public List<string> FailedIds { get; set; } = new();
    }
    #endregion

    #region IDisposable
    /// <summary>
    /// Releases resources used by the component.
    /// </summary>
    public void Dispose()
    {
        progressTimer?.Dispose();
    }
    #endregion
}
