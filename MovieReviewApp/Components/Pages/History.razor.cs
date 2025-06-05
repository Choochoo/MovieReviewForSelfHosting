
using Microsoft.AspNetCore.Components;
using MovieReviewApp.Models;
using MovieReviewApp.Services;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieReviewApp.Components.Pages
{
    public partial class History
    {
        [Inject]
        private MovieReviewService movieReviewService { get; set; } = default!;

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

        protected override void OnInitialized()
        {
            Pastevents = movieReviewService.GetAllMovieEvents();
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
}