using MovieReviewApp.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Pages
{
    public partial class History
    {
        public List<MovieEvent?> Pastevents;
        private MongoDb db = new MongoDb();
        protected override async Task OnInitializedAsync()
        {
            Pastevents = db.GetAllMovieEvents();
        }


    }
}