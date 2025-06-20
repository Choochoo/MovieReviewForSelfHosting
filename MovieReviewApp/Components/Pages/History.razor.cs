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

    // Computed property for sorted movie events (newest first)
    private List<MovieEvent> SortedMovieEvents =>
        Pastevents?.Where(e => e != null && !string.IsNullOrEmpty(e.Movie))
                  .OrderByDescending(e => e.StartDate)
                  .ToList() ?? new List<MovieEvent>();

    protected override async Task OnInitializedAsync()
    {
        Pastevents = await MovieEventService.GetAllAsync();
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
