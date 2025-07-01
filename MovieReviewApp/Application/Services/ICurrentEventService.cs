using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public interface ICurrentEventService
{
    Task<MovieEvent?> GetCurrentEventAsync();
    Task<MovieEvent?> GetNextEventAsync();
    Task<MovieEvent?> GetMostRecentPastEventAsync();
}