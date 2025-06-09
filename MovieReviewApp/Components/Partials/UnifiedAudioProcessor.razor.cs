using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Application.Services.Session;
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
    [Inject] private AudioProcessingWorkflowService WorkflowService { get; set; } = default!;
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
                    CurrentStep = "Waiting to upload",
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
        if (!CanCreateSession) return;
        
        isProcessing = true;
        errorMessage = string.Empty;
        
        try
        {
            // Step 1: Get selected movie details
            if (!selectedMovieId.HasValue)
            {
                throw new InvalidOperationException("No movie selected");
            }
            
            MovieEvent selectedMovie = availableMovies.First(m => m.Id == selectedMovieId.Value);
            
            // Step 2: Create session folder with proper naming convention for metadata extraction
            // Format: YYYY-MonthName-MovieTitle for automatic metadata parsing
            string folderName = $"{selectedMovie.StartDate:yyyy-MMMM}-{SanitizeFileName(selectedMovie.Movie)}";
            string baseUploadPath = Path.Combine(WebHostEnvironment.WebRootPath, "uploads");
            string sessionFolderPath = Path.Combine(baseUploadPath, folderName);
            
            // Step 3: Use AudioFileOrganizer to initialize session folder
            AudioOrganizer.InitializeAudioFolders(sessionFolderPath);
            
            // Step 4: Initialize all files to "Waiting to upload" status first
            foreach (var fileWrapper in uploadedFiles)
            {
                fileWrapper.ProcessingStatus = AudioProcessingStatus.Pending;
                fileWrapper.CurrentStep = "Waiting to upload";
                fileWrapper.ProgressPercentage = 0;
            }
            await InvokeAsync(StateHasChanged);
            
            // Small delay to show all files in "Waiting to upload" state
            await Task.Delay(500);
            
            // Step 5: Upload each file individually with progress tracking
            for (int i = 0; i < uploadedFiles.Count && i < selectedFiles.Count; i++)
            {
                var fileWrapper = uploadedFiles[i];
                var browserFile = selectedFiles[i];
                
                // Update this specific file to uploading status
                fileWrapper.ProcessingStatus = AudioProcessingStatus.Uploading;
                fileWrapper.CurrentStep = "Uploading...";
                fileWrapper.ProgressPercentage = 0;
                await InvokeAsync(StateHasChanged);
                
                string filePath = Path.Combine(sessionFolderPath, fileWrapper.FileName);
                
                // Save browser file to disk with progress tracking
                await using FileStream fs = new FileStream(filePath, FileMode.Create);
                using Stream browserStream = browserFile.OpenReadStream(maxAllowedSize: 10L * 1024 * 1024 * 1024);
                
                // Track progress during file copy
                long totalBytes = browserFile.Size;
                long copiedBytes = 0;
                byte[] buffer = new byte[81920]; // 80KB buffer for good performance
                int bytesRead;
                
                while ((bytesRead = await browserStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    copiedBytes += bytesRead;
                    
                    // Update progress every chunk for this specific file
                    int progressPercentage = (int)(copiedBytes * 100 / totalBytes);
                    if (progressPercentage != fileWrapper.ProgressPercentage)
                    {
                        fileWrapper.ProgressPercentage = progressPercentage;
                        fileWrapper.CurrentStep = $"Uploading... {FormatBytes(copiedBytes)} / {FormatBytes(totalBytes)}";
                        await InvokeAsync(StateHasChanged);
                    }
                }
                
                // Mark this specific file as ready for conversion and add proper metadata
                fileWrapper.FilePath = filePath;
                fileWrapper.ProcessingStatus = AudioProcessingStatus.Pending;
                fileWrapper.CurrentStep = "Ready to convert to MP3";
                fileWrapper.ProgressPercentage = 100;
                
                // Add metadata that would normally be set by PrepareSessionFromFolder
                FileInfo fileInfo = new FileInfo(filePath);
                fileWrapper.Id = Guid.NewGuid();
                fileWrapper.FileSize = fileInfo.Length;
                fileWrapper.SpeakerNumber = GetSpeakerNumberForFile(fileWrapper.FileName);
                fileWrapper.IsMasterRecording = fileWrapper.FileName.ToLowerInvariant().Contains("master");
                fileWrapper.LastUpdated = DateTime.UtcNow;
                
                await InvokeAsync(StateHasChanged);
                
                // Small delay to show "Ready to convert to MP3" state before moving to next file
                await Task.Delay(200);
            }
            
            // Step 5: Use PrepareSessionFromFolder to get proper file analysis, then merge with our uploaded files
            MovieSession preparedSession = await MovieSessionService.PrepareSessionFromFolder(sessionFolderPath, micAssignments);
            
            // Step 6: Merge the prepared session's file analysis with our uploaded files' progress
            foreach (var preparedFile in preparedSession.AudioFiles)
            {
                var uploadedFile = uploadedFiles.FirstOrDefault(uf => Path.GetFileName(uf.FilePath) == preparedFile.FileName);
                if (uploadedFile != null)
                {
                    // Keep our upload progress and status
                    preparedFile.CurrentStep = uploadedFile.CurrentStep;
                    preparedFile.ProgressPercentage = uploadedFile.ProgressPercentage;
                    
                    // But use the prepared file's analysis (master recording, speaker assignment, etc.)
                    uploadedFile.IsMasterRecording = preparedFile.IsMasterRecording;
                    uploadedFile.SpeakerNumber = preparedFile.SpeakerNumber;
                    uploadedFile.Id = preparedFile.Id;
                    uploadedFile.FileSize = preparedFile.FileSize;
                }
            }
            
            // Step 7: Create final session with proper metadata and our upload progress
            MovieSession newSession = new MovieSession
            {
                Id = preparedSession.Id,
                Date = selectedMovie.StartDate,
                MovieTitle = selectedMovie.Movie,
                FolderPath = sessionFolderPath,
                ParticipantsPresent = presentSpeakers.Select(id => micAssignments.GetValueOrDefault(id, $"Speaker {id}")).ToList(),
                MicAssignments = micAssignments,
                Status = ProcessingStatus.Pending,
                AudioFiles = preparedSession.AudioFiles, // Use the properly analyzed files
                CreatedAt = DateTime.UtcNow
            };
            
            // Step 8: Save session to database using existing service
            selectedSession = await MovieSessionService.SaveSessionToDatabase(newSession);
            
            // Step 9: Update UI with the properly analyzed files
            
            await InvokeAsync(StateHasChanged);
            
            // Step 10: Automatically start processing from WAV → MP3 conversion
            _ = Task.Run(async () =>
            {
                try
                {
                    // Small delay to let UI update and show the session processing view
                    await Task.Delay(500);
                    
                    // Start bulk processing from WAV to MP3 conversion
                    await StartBulkProcessing(AudioProcessingStatus.ConvertingToMp3);
                }
                catch (Exception ex)
                {
                    // Update error message on UI thread
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
            
            // Reset file statuses on error
            foreach (var file in uploadedFiles)
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
    /// Sanitizes a filename by removing invalid characters.
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Join("", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Replace(" ", "-"); // Replace spaces with hyphens for cleaner folder names
    }
    
    /// <summary>
    /// Determines the speaker number for a file based on naming patterns and mic assignments.
    /// </summary>
    private int? GetSpeakerNumberForFile(string fileName)
    {
        // Try to extract speaker number from filename patterns like "speaker1_", "mic2_", etc.
        foreach (var kvp in micAssignments)
        {
            if (fileName.ToLowerInvariant().Contains(kvp.Value.ToLowerInvariant()) ||
                fileName.ToLowerInvariant().Contains($"speaker{kvp.Key}") ||
                fileName.ToLowerInvariant().Contains($"mic{kvp.Key}"))
            {
                return kvp.Key;
            }
        }
        
        // Default to first present speaker if no pattern found
        return presentSpeakers.FirstOrDefault();
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
    /// <summary>
    /// Starts bulk processing of audio files from the specified status onwards.
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
            // Update UI to show processing has started
            await InvokeAsync(StateHasChanged);

            // Start processing based on the specified status
            switch (status)
            {
                case AudioProcessingStatus.ConvertingToMp3:
                    await ProcessFromWavConversion();
                    break;
                    
                case AudioProcessingStatus.UploadingToGladia:
                    await ProcessFromGladiaUpload();
                    break;
                    
                case AudioProcessingStatus.Transcribing:
                    await ProcessFromTranscription();
                    break;
                    
                default:
                    // For any other status, run the full enhanced workflow
                    await ProcessFullWorkflow();
                    break;
            }

            // Update session in database after processing
            if (selectedSession != null)
            {
                await DatabaseService.UpsertAsync(selectedSession);
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Bulk processing failed: {ex.Message}";
            
            // Update any files that were in progress back to a retryable state
            if (selectedSession?.AudioFiles != null)
            {
                foreach (var file in selectedSession.AudioFiles.Where(f => IsInProgress(f.ProcessingStatus)))
                {
                    file.ProcessingStatus = AudioProcessingStatus.Failed;
                    file.CurrentStep = "Processing interrupted";
                    file.CanRetry = true;
                }
            }
        }
        finally
        {
            isProcessing = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Processes files starting from WAV to MP3 conversion.
    /// </summary>
    private async Task ProcessFromWavConversion()
    {
        if (selectedSession == null) return;

        // Convert audio files
        await WorkflowService.ConvertAudioFilesAsync(selectedSession, (message, progress) =>
        {
            // Update UI with progress
            InvokeAsync(() =>
            {
                // Find files that are currently converting and update their status
                foreach (var file in selectedSession.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.ConvertingToMp3))
                {
                    file.CurrentStep = message;
                    file.ProgressPercentage = progress;
                }
                StateHasChanged();
                return Task.CompletedTask;
            });
        });

        // Continue to upload after conversion
        await ProcessFromGladiaUpload();
    }

    /// <summary>
    /// Processes files starting from Gladia upload.
    /// </summary>
    private async Task ProcessFromGladiaUpload()
    {
        if (selectedSession == null) return;

        // Upload to Gladia
        await WorkflowService.UploadAudioFilesAsync(selectedSession, (message, progress) =>
        {
            // Update UI with progress
            InvokeAsync(() =>
            {
                // Find files that are currently uploading and update their status
                foreach (var file in selectedSession.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.UploadingToGladia))
                {
                    file.CurrentStep = message;
                    file.ProgressPercentage = progress;
                }
                StateHasChanged();
                return Task.CompletedTask;
            });
        });

        // Continue to transcription after upload
        await ProcessFromTranscription();
    }

    /// <summary>
    /// Processes files starting from transcription.
    /// </summary>
    private async Task ProcessFromTranscription()
    {
        if (selectedSession == null) return;

        // Process transcriptions
        await WorkflowService.ProcessTranscriptionsAsync(selectedSession, (message, progress) =>
        {
            // Update UI with progress
            InvokeAsync(() =>
            {
                // Find files that are currently transcribing and update their status
                foreach (var file in selectedSession.AudioFiles.Where(f => f.ProcessingStatus == AudioProcessingStatus.Transcribing))
                {
                    file.CurrentStep = message;
                    file.ProgressPercentage = progress;
                }
                StateHasChanged();
                return Task.CompletedTask;
            });
        });
    }

    /// <summary>
    /// Processes files through the complete enhanced workflow.
    /// </summary>
    private async Task ProcessFullWorkflow()
    {
        if (selectedSession == null) return;

        await WorkflowService.ProcessSessionEnhancedAsync(selectedSession, (message, progress) =>
        {
            // Update UI with overall progress
            InvokeAsync(() =>
            {
                // Update all files with the overall progress message
                foreach (var file in selectedSession.AudioFiles)
                {
                    if (IsInProgress(file.ProcessingStatus))
                    {
                        file.CurrentStep = message;
                        file.ProgressPercentage = progress;
                    }
                }
                StateHasChanged();
                return Task.CompletedTask;
            });
        });
    }
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
        AudioProcessingStatus.Uploading => "📤",
        AudioProcessingStatus.ConvertingToMp3 => "🔄",
        AudioProcessingStatus.PendingMp3 => "⏳",
        AudioProcessingStatus.FailedMp3 => "❌",
        AudioProcessingStatus.ProcessedMp3 => "✅",
        AudioProcessingStatus.UploadingToGladia => "☁️",
        AudioProcessingStatus.UploadedToGladia => "☁️",
        AudioProcessingStatus.Transcribing => "✍️",
        AudioProcessingStatus.TranscriptionComplete => "📝",
        AudioProcessingStatus.ProcessingWithAI => "🤖",
        AudioProcessingStatus.Complete => "✅",
        AudioProcessingStatus.Failed => "❌",
        _ => "❓"
    };

    private string GetStatusText(AudioProcessingStatus status) => status switch
    {
        AudioProcessingStatus.Pending => "Waiting to upload",
        AudioProcessingStatus.Uploading => "Uploading...",
        AudioProcessingStatus.ConvertingToMp3 => "Converting to MP3...",
        AudioProcessingStatus.PendingMp3 => "Ready to upload to Gladia",
        AudioProcessingStatus.FailedMp3 => "MP3 conversion failed",
        AudioProcessingStatus.ProcessedMp3 => "Ready to upload to Gladia",
        AudioProcessingStatus.UploadingToGladia => "Uploading to Gladia...",
        AudioProcessingStatus.UploadedToGladia => "Ready to download transcriptions",
        AudioProcessingStatus.Transcribing => "Downloading transcriptions...",
        AudioProcessingStatus.TranscriptionComplete => "Ready to process transcriptions",
        AudioProcessingStatus.ProcessingWithAI => "Processing with AI...",
        AudioProcessingStatus.Complete => "Complete",
        AudioProcessingStatus.Failed => "Processing failed",
        _ => $"Status: {status}"
    };

    private bool IsInProgress(AudioProcessingStatus status) => status is 
        AudioProcessingStatus.Uploading or
        AudioProcessingStatus.ConvertingToMp3 or 
        AudioProcessingStatus.UploadingToGladia or 
        AudioProcessingStatus.Transcribing or 
        AudioProcessingStatus.ProcessingWithAI;

    private int GetOverallProgress()
    {
        if (selectedSession?.AudioFiles.Any() != true)
            return 0;

        var audioFiles = selectedSession.AudioFiles;
        var totalFiles = audioFiles.Count;
        
        // Calculate progress by considering both completed files and individual file progress
        var totalProgress = 0.0;
        
        foreach (var file in audioFiles)
        {
            if (file.ProcessingStatus == AudioProcessingStatus.Complete)
            {
                // Completed files contribute 100% of their weight
                totalProgress += 100.0;
            }
            else if (IsInProgress(file.ProcessingStatus))
            {
                // Files in progress contribute their individual progress percentage
                totalProgress += file.ProgressPercentage;
            }
            // Pending/failed files contribute 0% (no need to explicitly handle)
        }
        
        return (int)(totalProgress / totalFiles);
    }

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

    #region Gladia Purge Functionality - DANGER ZONE

    /// <summary>
    /// Enum for tracking the current step in the purge workflow
    /// </summary>
    private enum PurgeStep
    {
        Initial,
        Checking,
        Confirmation,
        Purging,
        Complete
    }

    // Purge-related state variables
    private bool showPurgeModal = false;
    private PurgeStep purgeStep = PurgeStep.Initial;
    private bool understandRisks = false;
    private bool isPurging = false;
    private string deleteConfirmation = string.Empty;
    private int transcriptionCount = 0;
    private int purgeProgress = 0;
    private List<TranscriptionListItem>? transcriptionsToDelete;
    private PurgeResult? purgeResult;

    /// <summary>
    /// Opens the purge modal and resets state
    /// </summary>
    private void ShowPurgeModal()
    {
        showPurgeModal = true;
        purgeStep = PurgeStep.Initial;
        understandRisks = false;
        deleteConfirmation = string.Empty;
        transcriptionCount = 0;
        purgeProgress = 0;
        transcriptionsToDelete = null;
        purgeResult = null;
        StateHasChanged();
    }

    /// <summary>
    /// Closes the purge modal
    /// </summary>
    private void ClosePurgeModal()
    {
        if (!isPurging)
        {
            showPurgeModal = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Checks what data exists in Gladia account
    /// </summary>
    private async Task CheckGladiaData()
    {
        purgeStep = PurgeStep.Checking;
        StateHasChanged();

        try
        {
            if (!GladiaService.IsConfigured)
            {
                errorMessage = "Gladia service is not configured. Please check your API key.";
                ClosePurgeModal();
                return;
            }

            // Get all transcriptions from Gladia
            List<TranscriptionListItem> allTranscriptions = new();
            int offset = 0;
            const int batchSize = 100;
            bool hasMore = true;

            while (hasMore)
            {
                List<TranscriptionListItem> batch = await GladiaService.ListAllTranscriptionsAsync(batchSize, offset);
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

            transcriptionsToDelete = allTranscriptions;
            transcriptionCount = allTranscriptions.Count;
            purgeStep = PurgeStep.Confirmation;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error checking Gladia data: {ex.Message}";
            ClosePurgeModal();
        }

        StateHasChanged();
    }

    /// <summary>
    /// Executes the purge operation with progress tracking
    /// </summary>
    private async Task ExecutePurge()
    {
        isPurging = true;
        purgeStep = PurgeStep.Purging;
        purgeProgress = 0;
        StateHasChanged();

        try
        {
            // Create confirmation callback that updates progress
            Func<int, int, Task<bool>> confirmationCallback = async (total, progress) =>
            {
                purgeProgress = progress;
                await InvokeAsync(StateHasChanged);
                return true; // Always confirm since user already confirmed
            };

            // Execute the purge with progress tracking
            purgeResult = await GladiaService.PurgeAllTranscriptionsAsync(async (total, progress) =>
            {
                purgeProgress = progress;
                await InvokeAsync(StateHasChanged);
                return true;
            });

            purgeStep = PurgeStep.Complete;
        }
        catch (Exception ex)
        {
            errorMessage = $"Critical error during purge: {ex.Message}";
            purgeResult = new PurgeResult
            {
                CriticalError = ex.Message,
                TotalFound = transcriptionCount
            };
            purgeStep = PurgeStep.Complete;
        }
        finally
        {
            isPurging = false;
            StateHasChanged();
        }
    }

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