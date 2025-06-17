using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class MonthlyDataGenerationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyDataGenerationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1); // Check every hour

    public MonthlyDataGenerationService(
        IServiceScopeFactory scopeFactory,
        ILogger<MonthlyDataGenerationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Monthly Data Generation Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndGenerateNextMonthData();
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Monthly Data Generation Service");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); // Retry in 30 minutes on error
            }
        }
    }

    private async Task CheckAndGenerateNextMonthData()
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        MongoDbService database = scope.ServiceProvider.GetRequiredService<MongoDbService>();
        
        DateTime now = DateTime.Now;
        DateTime firstOfThisMonth = new DateTime(now.Year, now.Month, 1);
        DateTime firstOfNextMonth = firstOfThisMonth.AddMonths(1);
        
        // Check if we already have data for next month
        List<MovieEvent> nextMonthEvents = await database.FindAsync<MovieEvent>(
            me => me.StartDate >= firstOfNextMonth && me.StartDate < firstOfNextMonth.AddMonths(1)
        );

        if (nextMonthEvents.Any())
        {
            // Data already exists for next month
            return;
        }

        // Check if it's a new month (1st-3rd of month, to ensure we catch it)
        if (now.Day <= 3)
        {
            await GenerateNextMonthData(database, firstOfNextMonth);
        }
    }

    private async Task GenerateNextMonthData(MongoDbService database, DateTime nextMonth)
    {
        _logger.LogInformation("Generating data for {Month:yyyy-MM}", nextMonth);

        try
        {
            // Get current phase structure to determine what should happen next month
            List<Phase> allPhases = (await database.GetAllAsync<Phase>())
                .OrderBy(p => p.Number)
                .ToList();

            List<MovieEvent> allEvents = (await database.GetAllAsync<MovieEvent>())
                .OrderBy(me => me.StartDate)
                .ToList();

            // Determine if next month is an award month or movie month
            bool isAwardMonth = await IsAwardMonth(database, nextMonth, allPhases);

            if (isAwardMonth)
            {
                await GenerateAwardEventForMonth(database, nextMonth, allPhases);
                _logger.LogInformation("Generated award event for {Month:yyyy-MM}", nextMonth);
            }
            else
            {
                await GenerateMovieEventForMonth(database, nextMonth, allPhases, allEvents);
                _logger.LogInformation("Generated movie event for {Month:yyyy-MM}", nextMonth);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating data for {Month:yyyy-MM}", nextMonth);
        }
    }

    private async Task<bool> IsAwardMonth(MongoDbService database, DateTime month, List<Phase> phases)
    {
        // Award months happen after every 2 phases
        // Check if this month falls in an award period
        
        foreach (Phase phase in phases.Where(p => p.Number % 2 == 0)) // Even-numbered phases
        {
            DateTime awardMonthStart = phase.EndDate.AddDays(1);
            DateTime awardMonthEnd = awardMonthStart.AddMonths(1);
            
            if (month >= awardMonthStart && month < awardMonthEnd)
            {
                return true;
            }
        }

        return false;
    }

    private async Task GenerateAwardEventForMonth(MongoDbService database, DateTime awardMonth, List<Phase> phases)
    {
        // Find the phase that this award month follows
        Phase? lastPhase = phases
            .Where(p => p.Number % 2 == 0 && p.EndDate < awardMonth)
            .OrderByDescending(p => p.Number)
            .FirstOrDefault();

        if (lastPhase == null) return;

        // Create award event
        AwardEvent awardEvent = new AwardEvent
        {
            StartDate = awardMonth,
            EndDate = awardMonth.AddMonths(1).AddDays(-1),
            VotingStartDate = awardMonth.AddDays(7),
            VotingEndDate = awardMonth.AddDays(21),
            PhaseNumber = lastPhase.Number
        };
        await database.UpsertAsync(awardEvent);

        // Create award questions/categories
        List<string> categories = new()
        {
            "Best Overall Film",
            "Most Surprising",
            "Biggest Disappointment",
            "Best Performance",
            "Most Rewatchable",
            "Marcus Chen Technical Excellence Award",
            "Sofia's Tearjerker Award",
            "David's Gore Glory Award"
        };

        foreach (string category in categories)
        {
            AwardQuestion question = new AwardQuestion
            {
                Question = category,
                MaxVotes = 1,
                IsActive = true
            };
            await database.UpsertAsync(question);
            
            awardEvent.Questions.Add(question.Id);
        }
        
        await database.UpsertAsync(awardEvent);
    }

    private async Task GenerateMovieEventForMonth(MongoDbService database, DateTime month, List<Phase> phases, List<MovieEvent> existingEvents)
    {
        // Determine which phase this month belongs to
        Phase? currentPhase = phases.FirstOrDefault(p => month >= p.StartDate && month <= p.EndDate);
        
        if (currentPhase == null)
        {
            // Need to create a new phase
            currentPhase = await CreateNewPhase(database, month, phases);
        }

        // Determine whose turn it is in this phase
        List<MovieEvent> phaseEvents = existingEvents
            .Where(me => me.PhaseNumber == currentPhase.Number)
            .OrderBy(me => me.StartDate)
            .ToList();

        string[] memberNames = { "Marcus Chen", "Sofia Rodriguez", "Amit Patel", "Rebecca Thompson", "Jamal Williams", "Elena Volkov", "David Park" };
        
        // Find who should select this month
        string selector = memberNames[phaseEvents.Count % memberNames.Length];

        // Generate movie event
        MovieEvent movieEvent = new MovieEvent
        {
            PhaseNumber = currentPhase.Number,
            Person = selector,
            Movie = await SelectRandomMovieForMember(selector),
            Reasoning = GenerateReasonForMember(selector),
            StartDate = month,
            EndDate = month.AddMonths(1).AddDays(-1),
            MeetupTime = GetLastFridayOfMonth(month).AddHours(19),
            AlreadySeen = false
        };

        await database.UpsertAsync(movieEvent);
    }

    private async Task<Phase> CreateNewPhase(MongoDbService database, DateTime month, List<Phase> existingPhases)
    {
        int newPhaseNumber = existingPhases.Any() ? existingPhases.Max(p => p.Number) + 1 : 1;
        
        Phase newPhase = new Phase
        {
            Number = newPhaseNumber,
            People = "Marcus Chen, Sofia Rodriguez, Amit Patel, Rebecca Thompson, Jamal Williams, Elena Volkov, David Park",
            StartDate = month,
            EndDate = month.AddMonths(7).AddDays(-1)
        };

        await database.UpsertAsync(newPhase);
        return newPhase;
    }

    private async Task<string> SelectRandomMovieForMember(string memberName)
    {
        // Simplified movie selection for background service
        Random random = new Random();
        
        Dictionary<string, string[]> memberMovies = new()
        {
            ["Marcus Chen"] = new[] { "Blade Runner 2049", "The Lighthouse", "Parasite", "Mad Max: Fury Road", "Her", "Arrival" },
            ["Sofia Rodriguez"] = new[] { "Lady Bird", "Moonlight", "The Shape of Water", "A Star Is Born", "Little Women", "Marriage Story" },
            ["Amit Patel"] = new[] { "Dune", "The Matrix", "Interstellar", "Inception", "Blade Runner", "The Terminator" },
            ["Rebecca Thompson"] = new[] { "Casablanca", "The Godfather", "Citizen Kane", "Vertigo", "Sunset Boulevard", "Roman Holiday" },
            ["Jamal Williams"] = new[] { "Avengers: Endgame", "The Dark Knight", "John Wick", "Mad Max: Fury Road", "Mission: Impossible", "Die Hard" },
            ["Elena Volkov"] = new[] { "Parasite", "Roma", "The Handmaiden", "Portrait of a Lady on Fire", "Burning", "Shoplifters" },
            ["David Park"] = new[] { "Hereditary", "Midsommar", "The Conjuring", "Get Out", "A Quiet Place", "The Babadook" }
        };

        string[] movies = memberMovies.GetValueOrDefault(memberName, new[] { "The Shawshank Redemption" });
        return movies[random.Next(movies.Length)];
    }

    private string GenerateReasonForMember(string memberName)
    {
        Dictionary<string, string[]> reasons = new()
        {
            ["Marcus Chen"] = new[] { "Technical masterpiece worth analyzing", "Cinematography deserves discussion", "Visual storytelling at its finest" },
            ["Sofia Rodriguez"] = new[] { "Emotional journey we all need to experience", "Performance-driven masterpiece", "Character development perfection" },
            ["Amit Patel"] = new[] { "Mind-bending concepts to explore", "Technology themes worth debating", "Future possibilities to discuss" },
            ["Rebecca Thompson"] = new[] { "Classic cinema education required", "Timeless storytelling", "Film history importance" },
            ["Jamal Williams"] = new[] { "Pure entertainment value", "Action-packed excitement", "Crowd-pleasing fun" },
            ["Elena Volkov"] = new[] { "International perspective needed", "Cultural significance", "Artistic vision worth exploring" },
            ["David Park"] = new[] { "Horror craftsmanship", "Genre excellence", "Suspense mastery" }
        };

        Random random = new Random();
        string[] memberReasons = reasons.GetValueOrDefault(memberName, new[] { "Great film worth watching" });
        return memberReasons[random.Next(memberReasons.Length)];
    }

    private DateTime GetLastFridayOfMonth(DateTime date)
    {
        DateTime lastDayOfMonth = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        
        while (lastDayOfMonth.DayOfWeek != DayOfWeek.Friday)
        {
            lastDayOfMonth = lastDayOfMonth.AddDays(-1);
        }
        
        return lastDayOfMonth;
    }
}