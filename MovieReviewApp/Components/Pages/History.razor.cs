
using Microsoft.AspNetCore.Components;
using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Pages
{
    public partial class History
    {
        [Inject]
        private MongoDb db { get; set; } = default!;

        public required List<MovieEvent> Pastevents { get; set; }
        
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
            Pastevents = db.GetAllMovieEvents();
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