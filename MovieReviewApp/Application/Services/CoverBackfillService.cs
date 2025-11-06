using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Models;
using MongoDB.Driver;

namespace MovieReviewApp.Application.Services;

public class CoverBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoverBackfillService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24); // Check daily
    private readonly TimeSpan _targetRunTime = new TimeSpan(3, 0, 0); // 3:00 AM

    public CoverBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<CoverBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cover Backfill Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Calculate next run time (3 AM)
                TimeSpan delay = CalculateDelayUntilNextRun();
                _logger.LogInformation("Next cover backfill check in {Hours} hours", delay.TotalHours);

                await Task.Delay(delay, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    await BackfillMissingCoversAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Cover Backfill Service");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // Retry in 1 hour on error
            }
        }
    }

    private TimeSpan CalculateDelayUntilNextRun()
    {
        DateTime now = DateTime.Now;
        DateTime nextRun = now.Date.Add(_targetRunTime);

        // If we've already passed 3 AM today, schedule for 3 AM tomorrow
        if (now > nextRun)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - now;
    }

    private async Task BackfillMissingCoversAsync()
    {
        _logger.LogInformation("Starting cover backfill check");

        using IServiceScope scope = _scopeFactory.CreateScope();
        MongoDbService database = scope.ServiceProvider.GetRequiredService<MongoDbService>();
        InstanceTypeService instanceTypeService = scope.ServiceProvider.GetRequiredService<InstanceTypeService>();
        TmdbService tmdbService = scope.ServiceProvider.GetRequiredService<TmdbService>();
        ImageService imageService = scope.ServiceProvider.GetRequiredService<ImageService>();

        // Only run for demo instances
        if (!instanceTypeService.ShouldGenerateDemoData())
        {
            _logger.LogInformation("Skipping cover backfill - not a demo instance");
            return;
        }

        // Find all MovieEvents without covers
        IMongoCollection<MovieEvent>? collection = database.GetCollection<MovieEvent>();
        if (collection == null)
        {
            _logger.LogWarning("MovieEvent collection not found");
            return;
        }

        List<MovieEvent> eventsWithoutCovers = await collection
            .Find(me => me.ImageId == null)
            .ToListAsync();

        if (!eventsWithoutCovers.Any())
        {
            _logger.LogInformation("No movie events missing covers");
            return;
        }

        _logger.LogInformation("Found {Count} movie events missing covers, starting backfill", eventsWithoutCovers.Count);

        int successCount = 0;
        int failureCount = 0;

        foreach (MovieEvent movieEvent in eventsWithoutCovers)
        {
            try
            {
                // If there's already a PosterUrl, try to download it directly
                if (!string.IsNullOrEmpty(movieEvent.PosterUrl))
                {
                    Guid? imageId = await imageService.SaveImageFromUrlAsync(movieEvent.PosterUrl);
                    if (imageId.HasValue)
                    {
                        movieEvent.ImageId = imageId;
                        movieEvent.PosterUrl = null;
                        await database.UpsertAsync(movieEvent);
                        successCount++;
                        _logger.LogInformation("Backfilled cover for {Movie} from existing URL", movieEvent.Movie);
                        continue;
                    }
                }

                // Otherwise, fetch from TMDB
                TmdbService.TmdbMovieInfo? tmdbInfo = await tmdbService.GetMovieInfoAsync(movieEvent.Movie);

                if (tmdbInfo != null && !string.IsNullOrEmpty(tmdbInfo.PosterUrl))
                {
                    Guid? imageId = await imageService.SaveImageFromUrlAsync(tmdbInfo.PosterUrl);
                    if (imageId.HasValue)
                    {
                        movieEvent.ImageId = imageId;

                        // Also update other TMDB fields if they're missing
                        if (string.IsNullOrEmpty(movieEvent.Synopsis))
                            movieEvent.Synopsis = tmdbInfo.Synopsis;
                        if (string.IsNullOrEmpty(movieEvent.IMDb))
                            movieEvent.IMDb = tmdbInfo.ImdbUrl;

                        await database.UpsertAsync(movieEvent);
                        successCount++;
                        _logger.LogInformation("Backfilled cover for {Movie} from TMDB", movieEvent.Movie);
                    }
                    else
                    {
                        failureCount++;
                        _logger.LogWarning("Failed to save image for {Movie}", movieEvent.Movie);
                    }
                }
                else
                {
                    failureCount++;
                    _logger.LogWarning("No TMDB info or poster found for {Movie}", movieEvent.Movie);
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                _logger.LogError(ex, "Error backfilling cover for {Movie}", movieEvent.Movie);
            }

            // Small delay to avoid overwhelming TMDB API
            await Task.Delay(250);
        }

        _logger.LogInformation(
            "Cover backfill completed: {Success} successful, {Failure} failed",
            successCount,
            failureCount
        );
    }
}
