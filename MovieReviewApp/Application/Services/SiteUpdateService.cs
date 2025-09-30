using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class SiteUpdateService : BaseService<SiteUpdate>
{
    public SiteUpdateService(IRepository<SiteUpdate> repository, ILogger<SiteUpdateService> logger)
        : base(repository, logger)
    {
    }

    public async Task AddSiteUpdateAsync(string description, string? username = null)
    {
        _ = await CreateAsync(new SiteUpdate
        {
            Description = description,
            UpdatedBy = username ?? "System",
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task<List<SiteUpdate>> GetRecentSiteUpdatesAsync(int count = 10)
    {
        List<SiteUpdate> updates = await GetAllAsync();
        return updates.OrderByDescending(u => u.Timestamp).Take(count).ToList();
    }


    public async Task<List<SiteUpdate>> GetRecentUpdatesAsync(DateTime lastVisit)
    {
        List<SiteUpdate> updates = await GetAllAsync();
        return updates.Where(u => u.Timestamp > lastVisit).ToList();
    }
} 