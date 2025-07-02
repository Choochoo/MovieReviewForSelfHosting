using Microsoft.AspNetCore.Components;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;

namespace MovieReviewApp.Components.Pages;

public partial class History : ComponentBase
{
    [Inject]
    private MovieEventService MovieEventService { get; set; } = default!;

    [Inject]
    private MarkdownService MarkdownService { get; set; } = default!;

    private List<MovieEvent> Pastevents { get; set; } = new();

    // New properties for theater view
    private bool isGridView = true; // Default to theater view as requested
    private MovieEvent? selectedMovie = null;
    private bool _isInitialized = false;

    // Computed property for sorted movie events (newest first)
    private List<MovieEvent> SortedMovieEvents =>
        Pastevents?.Where(e => e != null && !string.IsNullOrEmpty(e.Movie))
                  .OrderByDescending(e => e.StartDate)
                  .ToList() ?? new List<MovieEvent>();

    protected override async Task OnInitializedAsync()
    {
        // Load data first
        Pastevents = await MovieEventService.GetAllAsync();
        
        // Add a delay to make the loading state visible and smooth transition
        await Task.Delay(1000); // 1 second delay like home page
        
        // Ensure we're on the UI thread and trigger state change
        await InvokeAsync(() =>
        {
            _isInitialized = true;
            StateHasChanged();
        });
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            // Force theater view on mobile - this is a simple approach
            // In production, you might want to use JavaScript interop to check viewport
            isGridView = true;
        }
    }

    private void ToggleView(bool showGrid)
    {
        isGridView = showGrid;
        selectedMovie = null; // Close any open modal when switching views
    }

    private void ShowMovieDetails(MovieEvent movieEvent)
    {
        selectedMovie = movieEvent;
    }

    private void CloseModal()
    {
        selectedMovie = null;
    }
}
