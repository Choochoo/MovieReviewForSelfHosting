
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
        protected override void OnInitialized()
        {
            Pastevents = db.GetAllMovieEvents();
        }


    }
}