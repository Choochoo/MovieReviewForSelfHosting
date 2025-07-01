using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MovieReviewApp.Application.Services;

public class SiteUpdateService : BaseService<SiteUpdate>
{
    public SiteUpdateService(MongoDbService databaseService, ILogger<SiteUpdateService> logger)
        : base(databaseService, logger)
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