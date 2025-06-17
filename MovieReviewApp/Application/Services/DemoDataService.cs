using MovieReviewApp.Application.Models.OpenAI;
using MovieReviewApp.Application.Models.Transcription;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Models;
using System.Net.Http;

namespace MovieReviewApp.Application.Services;

public class DemoDataService
{
    private readonly MongoDbService _database;
    private readonly Random _random;
    private readonly TmdbService _tmdbService;
    private readonly ImageService _imageService;
    private readonly Dictionary<string, TmdbService.TmdbMovieInfo> _movieCache = new();
    private readonly HashSet<string> _usedMovies = new();
    
    // Fixed 7 members for consistency across 10 years
    private readonly List<DemoMember> _members = new()
    {
        new DemoMember
        {
            Name = "Marcus Chen",
            AgeAtStart = 28,
            Personality = "Film school graduate, technical analyzer",
            Preferences = new[] { "psychological thriller", "noir", "arthouse" },
            Quirks = "Always brings up aspect ratios",
            Snack = "Craft beer and pretzels",
            TalkativenessFactor = 0.8,
            CriticalnessFactor = 0.9
        },
        new DemoMember
        {
            Name = "Sofia Rodriguez",
            AgeAtStart = 34,
            Personality = "Former theater actress, emotional viewer",
            Preferences = new[] { "character drama", "musicals", "biopics" },
            Quirks = "Cries easily, quotes movies constantly",
            Snack = "Wine and cheese",
            TalkativenessFactor = 0.7,
            CriticalnessFactor = 0.4
        },
        new DemoMember
        {
            Name = "Amit Patel",
            AgeAtStart = 31,
            Personality = "Software engineer, loves sci-fi",
            Preferences = new[] { "science fiction", "cyberpunk", "time travel" },
            Quirks = "Calculates plot holes, loves diagrams",
            Snack = "Energy drinks and chips",
            TalkativenessFactor = 0.6,
            CriticalnessFactor = 0.8
        },
        new DemoMember
        {
            Name = "Rebecca Thompson",
            AgeAtStart = 42,
            Personality = "High school teacher, classic film buff",
            Preferences = new[] { "classics", "historical", "literary adaptations" },
            Quirks = "Compares everything to the book",
            Snack = "Tea and cookies",
            TalkativenessFactor = 0.9,
            CriticalnessFactor = 0.7
        },
        new DemoMember
        {
            Name = "Jamal Williams",
            AgeAtStart = 26,
            Personality = "Comedian, action movie enthusiast",
            Preferences = new[] { "action", "comedy", "superhero" },
            Quirks = "Makes jokes during serious scenes",
            Snack = "Popcorn with hot sauce",
            TalkativenessFactor = 0.8,
            CriticalnessFactor = 0.3
        },
        new DemoMember
        {
            Name = "Elena Volkov",
            AgeAtStart = 38,
            Personality = "Documentary filmmaker, world cinema lover",
            Preferences = new[] { "foreign films", "documentaries", "experimental" },
            Quirks = "Insists on subtitles even for English films",
            Snack = "Hummus and vegetables",
            TalkativenessFactor = 0.7,
            CriticalnessFactor = 0.8
        },
        new DemoMember
        {
            Name = "David Park",
            AgeAtStart = 29,
            Personality = "Horror fanatic, special effects artist",
            Preferences = new[] { "horror", "creature features", "b-movies" },
            Quirks = "Rates movies by 'gore factor'",
            Snack = "Red licorice and soda",
            TalkativenessFactor = 0.5,
            CriticalnessFactor = 0.6
        }
    };

    public DemoDataService(MongoDbService database, TmdbService tmdbService, ImageService imageService)
    {
        _database = database;
        _tmdbService = tmdbService;
        _imageService = imageService;
        _random = new Random(42); // Fixed seed for consistent data
    }

    public async Task GenerateDemoDataAsync()
    {
        DateTime now = DateTime.Now;
        DateTime endDate = now.AddMonths(2); // Include current and next 2 months to ensure current month shows
        DateTime startDate = now.AddYears(-2); // Only past 2 years
        
        Console.WriteLine($"📅 Generating demo data from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
        
        // Clear existing demo data and reset tracking
        _usedMovies.Clear();
        await ClearDemoDataAsync();
        
        // Create People records
        await CreateDemoPeopleAsync();
        
        // Generate phases and movie events (correct 15-month cycle logic)
        await GeneratePhasesAndMovieEventsAsync(startDate, endDate);
        
        // Generate movie sessions with data for completed months (only past months)
        await GenerateMovieSessionsAsync(startDate, now);
        
        // Generate awards for completed award cycles
        await GenerateAwardsAsync();
        
        Console.WriteLine("📅 Generated data through current month + next month for active home page");
    }

    private async Task ClearDemoDataAsync()
    {
        Console.WriteLine("🧹 Clearing existing demo data...");
        
        // Clear all collections for clean demo data
        List<Type> collectionsToClean = new()
        {
            typeof(Person), typeof(MovieEvent), typeof(Phase), 
            typeof(MovieSession), typeof(AwardEvent), typeof(AwardQuestion), typeof(AwardVote)
        };

        foreach (Type collectionType in collectionsToClean)
        {
            try
            {
                // Delete all documents from each collection
                long deletedCount = 0;
                if (collectionType == typeof(Person))
                    deletedCount = await _database.DeleteManyAsync<Person>(p => true);
                else if (collectionType == typeof(MovieEvent))
                    deletedCount = await _database.DeleteManyAsync<MovieEvent>(me => true);
                else if (collectionType == typeof(Phase))
                    deletedCount = await _database.DeleteManyAsync<Phase>(p => true);
                else if (collectionType == typeof(MovieSession))
                    deletedCount = await _database.DeleteManyAsync<MovieSession>(ms => true);
                else if (collectionType == typeof(AwardEvent))
                    deletedCount = await _database.DeleteManyAsync<AwardEvent>(ae => true);
                else if (collectionType == typeof(AwardQuestion))
                    deletedCount = await _database.DeleteManyAsync<AwardQuestion>(aq => true);
                else if (collectionType == typeof(AwardVote))
                    deletedCount = await _database.DeleteManyAsync<AwardVote>(av => true);
                
                Console.WriteLine($"   Cleared {deletedCount} records from {collectionType.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Error clearing {collectionType.Name}: {ex.Message}");
            }
        }
    }

    private async Task CreateDemoPeopleAsync()
    {
        Console.WriteLine("👥 Creating 7 AI members...");
        int order = 1;
        foreach (DemoMember member in _members)
        {
            Person person = new Person
            {
                Order = order++,
                Name = member.Name
            };
            await _database.UpsertAsync(person);
            Console.WriteLine($"   ✅ Created {member.Name} ({member.Personality})");
        }
    }

    private async Task GeneratePhasesAndMovieEventsAsync(DateTime startDate, DateTime endDate)
    {
        int phaseNumber = 1;
        DateTime currentDate = startDate;

        while (currentDate <= endDate)
        {
            // Create Phase 1 (7 months)
            Phase phase1 = new Phase
            {
                Number = phaseNumber,
                People = string.Join(", ", _members.Select(m => m.Name)),
                StartDate = currentDate,
                EndDate = currentDate.AddMonths(7).AddDays(-1)
            };
            await _database.UpsertAsync(phase1);

            // Generate movie events for Phase 1
            for (int i = 0; i < 7 && currentDate <= endDate; i++)
            {
                DemoMember selector = _members[i];
                
                string movieTitle = await SelectMovieForMemberAsync(selector, currentDate);
                TmdbService.TmdbMovieInfo? tmdbInfo = await GetOrFetchMovieInfoAsync(movieTitle);
                
                MovieEvent movieEvent = new MovieEvent
                {
                    PhaseNumber = phaseNumber,
                    Person = selector.Name,
                    Movie = tmdbInfo?.Title ?? movieTitle,
                    Reasoning = GenerateSelectionReason(selector, movieTitle, tmdbInfo),
                    StartDate = currentDate,
                    EndDate = currentDate.AddMonths(1).AddDays(-1),
                    MeetupTime = GenerateRealisticMeetupTime(currentDate),
                    AlreadySeen = GenerateAlreadySeen(selector, tmdbInfo),
                    SeenDate = GenerateSeenDate(tmdbInfo, currentDate),
                    Synopsis = tmdbInfo?.Synopsis,
                    IMDb = tmdbInfo?.ImdbUrl,
                    PosterUrl = tmdbInfo?.PosterUrl
                };
                
                // Download and store poster image
                if (!string.IsNullOrEmpty(movieEvent.PosterUrl))
                {
                    try
                    {
                        Guid? imageId = await _imageService.SaveImageFromUrlAsync(movieEvent.PosterUrl);
                        if (imageId.HasValue)
                        {
                            movieEvent.ImageId = imageId;
                            movieEvent.PosterUrl = null; // Clear URL after storing
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ⚠️ Failed to download poster for {movieTitle}: {ex.Message}");
                    }
                }
                
                await _database.UpsertAsync(movieEvent);
                currentDate = currentDate.AddMonths(1);
            }
            
            phaseNumber++;

            // Create Phase 2 (7 months)
            if (currentDate <= endDate)
            {
                Phase phase2 = new Phase
                {
                    Number = phaseNumber,
                    People = string.Join(", ", _members.Select(m => m.Name)),
                    StartDate = currentDate,
                    EndDate = currentDate.AddMonths(7).AddDays(-1)
                };
                await _database.UpsertAsync(phase2);

                // Generate movie events for Phase 2
                for (int i = 0; i < 7 && currentDate <= endDate; i++)
                {
                    DemoMember selector = _members[i];
                    
                    string movieTitle = await SelectMovieForMemberAsync(selector, currentDate);
                    TmdbService.TmdbMovieInfo? tmdbInfo = await GetOrFetchMovieInfoAsync(movieTitle);
                    
                    MovieEvent movieEvent = new MovieEvent
                    {
                        PhaseNumber = phaseNumber,
                        Person = selector.Name,
                        Movie = tmdbInfo?.Title ?? movieTitle,
                        Reasoning = GenerateSelectionReason(selector, movieTitle, tmdbInfo),
                        StartDate = currentDate,
                        EndDate = currentDate.AddMonths(1).AddDays(-1),
                        MeetupTime = GenerateRealisticMeetupTime(currentDate),
                        AlreadySeen = GenerateAlreadySeen(selector, tmdbInfo),
                        SeenDate = GenerateSeenDate(tmdbInfo, currentDate),
                        Synopsis = tmdbInfo?.Synopsis,
                        IMDb = tmdbInfo?.ImdbUrl,
                        PosterUrl = tmdbInfo?.PosterUrl
                    };
                    
                    // Download and store poster image
                    if (!string.IsNullOrEmpty(movieEvent.PosterUrl))
                    {
                        try
                        {
                            Guid? imageId = await _imageService.SaveImageFromUrlAsync(movieEvent.PosterUrl);
                            if (imageId.HasValue)
                            {
                                movieEvent.ImageId = imageId;
                                movieEvent.PosterUrl = null; // Clear URL after storing
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"   ⚠️ Failed to download poster for {movieTitle}: {ex.Message}");
                        }
                    }
                    
                    await _database.UpsertAsync(movieEvent);
                    currentDate = currentDate.AddMonths(1);
                }
                
                phaseNumber++;
                
                // Award month - create AwardEvent for this month
                if (currentDate <= endDate)
                {
                    await CreateAwardEventForMonth(currentDate, phaseNumber - 1);
                    currentDate = currentDate.AddMonths(1);
                }
            }
        }
    }

    private DateTime GenerateRealisticMeetupTime(DateTime monthDate)
    {
        // Get last Friday of the month
        DateTime lastDayOfMonth = new DateTime(monthDate.Year, monthDate.Month, DateTime.DaysInMonth(monthDate.Year, monthDate.Month));
        while (lastDayOfMonth.DayOfWeek != DayOfWeek.Friday)
        {
            lastDayOfMonth = lastDayOfMonth.AddDays(-1);
        }
        
        // Generate realistic meetup times
        bool isWeekend = lastDayOfMonth.DayOfWeek == DayOfWeek.Saturday || lastDayOfMonth.DayOfWeek == DayOfWeek.Sunday;
        
        if (isWeekend)
        {
            // Weekend: noon to 7pm (12:00 - 19:00)
            int[] weekendHours = { 12, 13, 14, 15, 16, 17, 18, 19 };
            int hour = weekendHours[_random.Next(weekendHours.Length)];
            return lastDayOfMonth.Date.AddHours(hour);
        }
        else
        {
            // Weekday: 5pm to 10pm (17:00 - 22:00)
            int[] weekdayHours = { 17, 18, 19, 20, 21, 22 };
            int hour = weekdayHours[_random.Next(weekdayHours.Length)];
            return lastDayOfMonth.Date.AddHours(hour);
        }
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

    private async Task<string> SelectMovieForMemberAsync(DemoMember member, DateTime selectionDate)
    {
        // Massive movie datasets - need 100+ movies each for 2 years of data
        await Task.CompletedTask; // Placeholder for async signature
        
        Dictionary<string, List<string>> memberMovies = new()
        {
            ["Marcus Chen"] = new() { 
                // Arthouse & Technical Excellence
                "Blade Runner 2049", "The Lighthouse", "Parasite", "Mad Max: Fury Road", "Her", "Arrival", "Ex Machina", "The Favourite", 
                "Stalker", "2001: A Space Odyssey", "Lawrence of Arabia", "The Master", "There Will Be Blood", "No Country for Old Men",
                "Apocalypse Now", "Barry Lyndon", "Days of Heaven", "The Tree of Life", "Mulholland Drive", "Lost Highway",
                "The Revenant", "1917", "Dunkirk", "Interstellar", "Gravity", "Children of Men", "The Road", "Annihilation",
                "Under the Skin", "Drive", "Only God Forgives", "The Neon Demon", "Suspiria", "Mandy", "Color Out of Space",
                "Midsommar", "Hereditary", "The Witch", "Black Swan", "Requiem for a Dream", "Mother!", "Prisoners", "Sicario",
                "Incendies", "Polytechnique", "First Man", "Chivo Lubezki", "Hoyte van Hoytema", "Greig Fraser", "Dune", "Dune: Part Two", "Top Gun: Maverick",
                "Everything Everywhere All at Once", "The Banshees of Inisherin", "Tár", "Women Talking", "Aftersun", "Nope"
            },
            ["Sofia Rodriguez"] = new() { 
                // Character Drama & Emotional Stories (removed duplicate "Nomadland")
                "Lady Bird", "Moonlight", "The Shape of Water", "A Star Is Born", "Little Women", "Marriage Story", "La La Land", "Nomadland",
                "Manchester by the Sea", "Room", "Brooklyn", "Carol", "Blue Is the Warmest Color", "Call Me by Your Name", "Portrait of a Lady on Fire",
                "Amour", "45 Years", "Still Alice", "The Danish Girl", "The Theory of Everything", "Me and Earl and the Dying Girl",
                "Inside Out", "Coco", "Soul", "Turning Red", "The Farewell", "Minari",
                "Sound of Metal", "CODA", "The Father", "Promising Young Woman", "Hillbilly Elegy", "Ma Rainey's Black Bottom",
                "One Night in Miami", "Judas and the Black Messiah", "The United States vs. Billie Holiday", "Malcolm & Marie",
                "Pieces of a Woman", "I'm Thinking of Ending Things", "His House", "The Half of It", "The Prom", "Eurovision Song Contest",
                "Hamilton", "In the Heights", "West Side Story", "tick, tick... BOOM!", "The Power of the Dog", "Spencer", "House of Gucci"
            },
            ["Amit Patel"] = new() { 
                // Sci-Fi & Tech Thrillers
                "Dune", "The Matrix", "Interstellar", "Inception", "Blade Runner", "The Terminator", "Ghost in the Shell", "Akira",
                "Ex Machina", "Transcendence", "The Machine", "I, Robot", "Minority Report", "Total Recall", "Strange Days",
                "eXistenZ", "The Thirteenth Floor", "Dark City", "Gattaca", "Elysium", "District 9", "Chappie", "Upgrade", "Archive",
                "Alita: Battle Angel", "Ready Player One", "The Meg", "Pacific Rim", "Edge of Tomorrow", "Source Code", "Looper",
                "Predestination", "The Butterfly Effect", "Primer", "Timecrimes", "Triangle", "Coherence", "The One I Love",
                "Moon", "Oblivion", "Tron: Legacy", "The Island", "Surrogates", "In Time", "The Adjustment Bureau", "Lucy",
                "Limitless", "The Prestige", "Shutter Island", "Memento", "Donnie Darko", "Pi", "Black Mirror Bandersnatch"
            },
            ["Rebecca Thompson"] = new() { 
                // Classics & Literary Adaptations
                "Casablanca", "The Godfather", "Citizen Kane", "Vertigo", "Sunset Boulevard", "The Treasure of the Sierra Madre", "Roman Holiday", "Rebecca",
                "Gone with the Wind", "Singin' in the Rain", "Some Like It Hot", "The Apartment", "Psycho", "North by Northwest", "Rear Window",
                "The Philadelphia Story", "His Girl Friday", "Bringing Up Baby", "It Happened One Night", "Mr. Smith Goes to Washington",
                "The Best Years of Our Lives", "All About Eve", "A Streetcar Named Desire", "On the Waterfront", "12 Angry Men",
                "The Bridge on the River Kwai", "Dr. Zhivago", "My Fair Lady", "The Sound of Music", "Mary Poppins",
                "Pride and Prejudice (1995)", "Sense and Sensibility", "Emma", "Jane Eyre", "Wuthering Heights", "Little Women (1994)",
                "Great Expectations", "Oliver Twist", "A Tale of Two Cities", "David Copperfield", "The Age of Innocence", "Howard's End",
                "A Room with a View", "The Remains of the Day", "The English Patient", "Cold Mountain", "Atonement", "Anna Karenina"
            },
            ["Jamal Williams"] = new() { 
                // Action & Comedy (removed duplicate "Mad Max: Fury Road")
                "Avengers: Endgame", "Spider-Man: Into the Spider-Verse", "The Dark Knight", "John Wick", "Mission: Impossible", "Die Hard", "Rush Hour",
                "Fast & Furious", "The Fast and the Furious", "2 Fast 2 Furious", "Fast Five", "Fast & Furious 6", "Furious 7", "The Fate of the Furious", "Hobbs & Shaw",
                "Marvel Cinematic Universe", "Iron Man", "Captain America", "Thor", "Guardians of the Galaxy", "Black Panther", "Doctor Strange", "Spider-Man",
                "Deadpool", "Logan", "X-Men", "Fantastic Four", "The Avengers", "Age of Ultron", "Civil War", "Infinity War", "Ant-Man", "Captain Marvel",
                "Shang-Chi", "Eternals", "No Way Home", "Multiverse of Madness", "Love and Thunder", "Wakanda Forever", "The Marvels",
                "DC Extended Universe", "Man of Steel", "Batman v Superman", "Wonder Woman", "Justice League", "Aquaman", "Shazam!", "Birds of Prey",
                "The Suicide Squad", "Black Adam", "The Flash", "Blue Beetle", "Transformers", "G.I. Joe", "The Rock Movies", "Rampage", "Skyscraper"
            },
            ["Elena Volkov"] = new() { 
                // World Cinema & Documentaries (removed duplicate "Shoplifters")
                "Roma", "The Handmaiden", "Portrait of a Lady on Fire", "Burning", "Shoplifters", "The Square", "Force Majeure",
                "The Hunt", "A Separation", "The Salesman", "About Elly", "A Hero", "The Past", "Fireworks Wednesday",
                "4 Months, 3 Weeks and 2 Days", "Beyond the Hills", "Graduation", "RMN", "Touch Me Not", "Bad Luck Banging or Loony Porn",
                "Drive My Car", "Wheel of Fortune and Fantasy", "Happy Hour", "Asako I & II", "Nobody Knows", "After the Storm",
                "Our Little Sister", "Like Father, Like Son", "Still Walking", "Air Doll", "13 Assassins", "Hara-Kiri: Death of a Samurai",
                "The Wind Rises", "Spirited Away", "Princess Mononoke", "My Neighbor Totoro", "Howl's Moving Castle", "Castle in the Sky",
                "Tampopo", "Ikiru", "Seven Samurai", "Rashomon", "Yojimbo", "Sanjuro", "The Hidden Fortress", "High and Low", "Red Beard"
            },
            ["David Park"] = new() { 
                // Horror & Genre Films (removed duplicates "Midsommar", "Black Swan", "Mother!", "Nope")
                "Hereditary", "The Conjuring", "Get Out", "A Quiet Place", "The Babadook", "It Follows",
                "X", "Pearl", "Scream", "Halloween", "Friday the 13th", "A Nightmare on Elm Street",
                "The Texas Chain Saw Massacre", "The Exorcist", "The Omen", "Rosemary's Baby", "Don't Look Now", "The Wicker Man", "Carrie",
                "The Shining", "Poltergeist", "An American Werewolf in London", "The Thing", "They Live", "Prince of Darkness", "In the Mouth of Madness",
                "Dead Alive", "Evil Dead", "Army of Darkness", "The Re-Animator", "From Beyond", "Society", "The Fly", "Videodrome", "Scanners",
                "Cronenberg Body Horror", "Clive Barker Films", "Hellraiser", "Candyman", "Lord of Illusions", "Nightbreed", "The Descent",
                "Dog Soldiers", "28 Days Later", "28 Weeks Later", "Shaun of the Dead", "Hot Fuzz", "The World's End", "Attack the Block"
            }
        };

        List<string> possibleMovies = memberMovies.GetValueOrDefault(member.Name, new List<string> { "The Shawshank Redemption" });
        
        // Filter out already used movies
        List<string> availableMovies = possibleMovies.Where(movie => !_usedMovies.Contains(movie)).ToList();
        
        // If no movies left for this member, use any remaining from other members
        if (!availableMovies.Any())
        {
            List<string> allMovies = memberMovies.Values.SelectMany(movies => movies).ToList();
            availableMovies = allMovies.Where(movie => !_usedMovies.Contains(movie)).ToList();
        }
        
        // If still no movies, fall back to a default
        if (!availableMovies.Any())
        {
            availableMovies = new List<string> { "The Shawshank Redemption", "Citizen Kane", "The Godfather" };
        }
        
        string selectedMovie = availableMovies[_random.Next(availableMovies.Count)];
        _usedMovies.Add(selectedMovie);
        
        return selectedMovie;
    }

    private async Task<TmdbService.TmdbMovieInfo?> GetOrFetchMovieInfoAsync(string movieTitle)
    {
        if (_movieCache.ContainsKey(movieTitle))
        {
            return _movieCache[movieTitle];
        }

        Console.WriteLine($"   📡 Fetching TMDB data for '{movieTitle}'...");
        TmdbService.TmdbMovieInfo? movieInfo = await _tmdbService.GetMovieInfoAsync(movieTitle);
        
        if (movieInfo != null)
        {
            _movieCache[movieTitle] = movieInfo;
        }

        return movieInfo;
    }

    private string GenerateSelectionReason(DemoMember member, string movieTitle, TmdbService.TmdbMovieInfo? tmdbInfo)
    {
        // Generate much more human, specific, and varied reasons
        List<string> reasonStarters = new()
        {
            $"Okay so I literally just watched {movieTitle} last week and I CANNOT stop thinking about it.",
            $"Listen, I know some of you might roll your eyes, but {movieTitle} is absolutely brilliant and here's why.",
            $"I've been meaning to share {movieTitle} with you guys for MONTHS and I finally have the perfect excuse.",
            $"So my sister recommended {movieTitle} and at first I was skeptical, but wow.",
            $"I randomly caught {movieTitle} on Netflix at 2am and it completely blew my mind.",
            $"You know how I'm always talking about great filmmaking? Well, {movieTitle} is exactly what I mean.",
            $"I saw {movieTitle} in theaters when it came out and it's been haunting me ever since.",
            $"My film professor used to rave about {movieTitle} and I finally understand the hype.",
            $"I found {movieTitle} in a '10 movies that will change your life' list and decided to test that theory."
        };

        Dictionary<string, List<string>> memberSpecificMiddles = new()
        {
            ["Marcus Chen"] = new()
            {
                $"The cinematography is absolutely gorgeous - every single frame looks like it belongs in a museum. The way they use light and shadow to tell the story is just *chef's kiss*.",
                $"I keep pausing scenes to analyze the camera movements because they're doing things I've never seen before. It's like film school in the best possible way.",
                $"The director clearly knows their craft because every shot feels intentional and meaningful. No wasted moments, pure visual storytelling.",
                $"I've watched the behind-the-scenes stuff and the technical innovation here is groundbreaking. We need to discuss how they achieved some of these shots."
            },
            ["Sofia Rodriguez"] = new()
            {
                $"I'm not even kidding, I sobbed for like an hour after watching this. The emotional weight is just incredible and I need you all to experience this with me.",
                $"The performances are so raw and honest that I forgot I was watching actors. Like, this is what cinema should be - pure human connection.",
                $"I literally called my mom crying after this movie because it hit me so hard emotionally. The way it explores relationships is just beautiful.",
                $"Warning: bring tissues because this will destroy you in the best way. But also it's so cathartic and healing at the same time."
            },
            ["Amit Patel"] = new()
            {
                $"The science is surprisingly accurate and they actually consulted real experts. I've been down a Wikipedia rabbit hole for hours fact-checking everything.",
                $"This is what good sci-fi should be - using technology and science to explore what it means to be human. Plus the plot holes are minimal!",
                $"I love how they don't dumb down the complex concepts. It treats the audience like we're intelligent enough to follow along with advanced ideas.",
                $"The way they visualize abstract concepts is brilliant. Finally, a movie that respects both science and storytelling equally."
            },
            ["Rebecca Thompson"] = new()
            {
                $"This is cinema history in the making, folks. I can already see how this will influence filmmakers for decades to come.",
                $"The literary references are so cleverly woven in - it's like a love letter to classic storytelling while being completely modern.",
                $"I've been teaching film for years and this is exactly the kind of movie that reminds me why I fell in love with cinema in the first place.",
                $"The way it honors classic filmmaking while pushing boundaries is masterful. It's both timeless and completely contemporary."
            },
            ["Jamal Williams"] = new()
            {
                $"The action sequences are INSANE and the comedy timing is perfect. It's exactly what we need after last month's depressing choice (looking at you, Sofia).",
                $"I was genuinely laughing out loud and pumping my fist during the fight scenes. Pure entertainment that doesn't insult your intelligence.",
                $"This movie knows exactly what it is and delivers on every level. Sometimes you just need two hours of pure fun, you know?",
                $"The stunts are practical, the jokes land perfectly, and nobody dies tragically at the end. What more could we ask for?"
            },
            ["Elena Volkov"] = new()
            {
                $"This won the Palme d'Or for a reason - it's the kind of bold, uncompromising filmmaking that American studios are too scared to finance.",
                $"The cultural specificity is what makes it universally relatable. This is how you make a film that speaks to the human condition.",
                $"I've been following this director since their early shorts and seeing them finally get the recognition they deserve is incredible.",
                $"The way it subverts your expectations while honoring the genre is exactly why international cinema is so vital to our understanding of film."
            },
            ["David Park"] = new()
            {
                $"The practical effects are absolutely bonkers and the gore is surprisingly artistic. This is horror done right - scary AND meaningful.",
                $"I've been saving this one for months because I knew it would be perfect for our group. The scares are earned and the atmosphere is suffocating.",
                $"The creature design is nightmare fuel in the best way. I love how they build tension without relying on cheap jump scares.",
                $"This director understands that the best horror comes from character and atmosphere, not just blood and gore. Though there's plenty of that too."
            }
        };

        Dictionary<string, List<string>> memberSpecificEndings = new()
        {
            ["Marcus Chen"] = new()
            {
                "Trust me, we're going to have SO much to unpack visually.",
                "I already have like fifteen screenshots I want to analyze with you all.",
                "This is the kind of filmmaking that reminds you why movies matter.",
                "Plus I promise there's enough technical stuff to keep us talking for hours."
            },
            ["Sofia Rodriguez"] = new()
            {
                "I need emotional support and you're all required to provide it.",
                "Just... please don't judge me when I start crying during the opening credits.",
                "I guarantee this will stay with you for weeks after watching.",
                "Also, we're definitely ordering comfort food for this discussion."
            },
            ["Amit Patel"] = new()
            {
                "I promise the science won't go over anyone's head, but it might blow your mind.",
                "I've already prepared a list of research papers if anyone wants to go deeper.",
                "This is what happens when writers actually do their homework.",
                "Plus the logical consistency is surprisingly solid throughout."
            },
            ["Rebecca Thompson"] = new()
            {
                "This is required viewing for anyone who loves cinema, period.",
                "I'm putting this on my syllabus next semester, that's how good it is.",
                "Sometimes a movie comes along that reminds you why film is an art form.",
                "You'll thank me for this choice, I absolutely guarantee it."
            },
            ["Jamal Williams"] = new()
            {
                "Sometimes you just need to turn your brain off and have a good time!",
                "I promise this will cleanse your palate after whatever pretentious thing we watched last.",
                "This is pure crowd-pleasing entertainment done at the highest level.",
                "Plus there are at least three moments that will make you cheer out loud."
            },
            ["Elena Volkov"] = new()
            {
                "This is why we need diverse voices in cinema - fresh perspectives change everything.",
                "I'm excited to discuss the cultural context because there's so much to unpack.",
                "This director is going to be huge, mark my words.",
                "Get ready to add another international filmmaker to your must-watch list."
            },
            ["David Park"] = new()
            {
                "I promise the scares are worth it and nobody will need therapy afterward.",
                "This is going to be so much fun to watch together and dissect the horror techniques.",
                "The practical effects alone make this worth our time.",
                "Plus I've been too nice with my selections lately - time to embrace the darkness."
            }
        };

        // Build complete human reasoning
        string starter = reasonStarters[_random.Next(reasonStarters.Count)];
        
        List<string> middles = memberSpecificMiddles.GetValueOrDefault(member.Name, new List<string>
        {
            "The writing is solid, the performances are great, and it's exactly the kind of movie that makes for good discussion."
        });
        string middle = middles[_random.Next(middles.Count)];
        
        List<string> endings = memberSpecificEndings.GetValueOrDefault(member.Name, new List<string>
        {
            "I think you'll all really enjoy this one!"
        });
        string ending = endings[_random.Next(endings.Count)];
        
        return $"{starter} {middle} {ending}";
    }

    private bool GenerateAlreadySeen(DemoMember member, TmdbService.TmdbMovieInfo? tmdbInfo)
    {
        // Base probability based on member's viewing habits
        double baseAlreadySeenChance = member.Name switch
        {
            "Marcus Chen" => 0.4,     // Film school graduate - sees lots of arthouse
            "Sofia Rodriguez" => 0.3,  // Former actress - sees character dramas
            "Amit Patel" => 0.35,     // Sci-fi enthusiast - knows the genre well
            "Rebecca Thompson" => 0.6, // Teacher - most likely to have seen classics
            "Jamal Williams" => 0.5,   // Comedian - follows popular action/comedy
            "Elena Volkov" => 0.45,    // Filmmaker - international festival circuit
            "David Park" => 0.4,       // Horror fanatic - knows the genre
            _ => 0.3
        };

        // Adjust based on movie age (older movies more likely to be seen)
        if (tmdbInfo?.ReleaseDate.HasValue == true)
        {
            int movieAge = DateTime.Now.Year - tmdbInfo.ReleaseDate.Value.Year;
            if (movieAge > 10) baseAlreadySeenChance += 0.3;
            else if (movieAge > 5) baseAlreadySeenChance += 0.2;
            else if (movieAge > 2) baseAlreadySeenChance += 0.1;
        }

        // Popular movies more likely to be seen
        if (tmdbInfo?.VoteAverage.HasValue == true && tmdbInfo.VoteAverage > 8.0)
        {
            baseAlreadySeenChance += 0.2;
        }

        return _random.NextDouble() < Math.Min(baseAlreadySeenChance, 0.8);
    }

    private DateTime? GenerateSeenDate(TmdbService.TmdbMovieInfo? tmdbInfo, DateTime selectionDate)
    {
        if (tmdbInfo?.ReleaseDate.HasValue != true) return null;

        DateTime earliestPossibleDate = tmdbInfo.ReleaseDate.Value;
        DateTime latestPossibleDate = DateTime.Now.AddMonths(-3); // Current date minus 3 months

        // If the movie was released after the latest possible date, return null
        if (earliestPossibleDate > latestPossibleDate) return null;

        // Generate random date between release and 3 months ago
        int totalDays = (latestPossibleDate - earliestPossibleDate).Days;
        if (totalDays <= 0) return null;

        int randomDays = _random.Next(0, totalDays + 1);
        return earliestPossibleDate.AddDays(randomDays);
    }

    private async Task GenerateMovieSessionsAsync(DateTime startDate, DateTime endDate)
    {
        List<MovieEvent> movieEvents = await _database.GetAllAsync<MovieEvent>();
        
        foreach (MovieEvent movieEvent in movieEvents)
        {
            if (movieEvent.MeetupTime.HasValue && movieEvent.MeetupTime.Value <= DateTime.Now.AddMonths(1))
            {
                MovieSession session = new MovieSession
                {
                    Date = movieEvent.MeetupTime.Value,
                    MovieTitle = movieEvent.Movie ?? "Unknown Movie",
                    FolderPath = $"demo/sessions/{movieEvent.MeetupTime.Value:yyyy-MM-dd}",
                    Status = ProcessingStatus.Complete,
                    ProcessedAt = movieEvent.MeetupTime.Value.AddDays(1),
                    SessionStats = GenerateSessionStats(movieEvent),
                    CategoryResults = GenerateCategoryResults(movieEvent),
                    MicAssignments = GenerateMicAssignments(),
                    AudioFiles = GenerateAudioFiles(movieEvent)
                };
                
                await _database.UpsertAsync(session);
            }
        }
    }

    private SessionStats GenerateSessionStats(MovieEvent movieEvent)
    {
        List<DemoMember> attendees = GenerateAttendees();
        int discussionMinutes = _random.Next(45, 120);
        
        return new SessionStats
        {
            TotalDuration = TimeSpan.FromMinutes(discussionMinutes).ToString(),
            EnergyLevel = (EnergyLevel)_random.Next(0, 3),
            TechnicalQuality = "Clear audio with minimal background noise",
            HighlightMoments = _random.Next(3, 8),
            BestMomentsSummary = GenerateBestMomentsSummary(movieEvent.Movie ?? "the movie"),
            WordCounts = GenerateWordCounts(attendees),
            QuestionCounts = GenerateQuestionCounts(attendees),
            InterruptionCounts = GenerateInterruptionCounts(attendees),
            LaughterCounts = GenerateLaughterCounts(attendees),
            CurseWordCounts = GenerateCurseWordCounts(attendees),
            MostTalkativePerson = attendees.OrderByDescending(a => a.TalkativenessFactor).First().Name,
            QuietestPerson = attendees.OrderBy(a => a.TalkativenessFactor).First().Name,
            MostInquisitivePerson = attendees[_random.Next(attendees.Count)].Name,
            BiggestInterruptor = attendees.OrderByDescending(a => a.TalkativenessFactor * _random.NextDouble()).First().Name,
            FunniestPerson = "Jamal Williams",
            TotalInterruptions = attendees.Sum(a => (int)(a.TalkativenessFactor * _random.Next(5, 15))),
            TotalQuestions = attendees.Sum(a => _random.Next(2, 8)),
            TotalLaughterMoments = _random.Next(10, 30),
            ConversationTone = GenerateConversationTone()
        };
    }

    private CategoryResults GenerateCategoryResults(MovieEvent movieEvent)
    {
        return new CategoryResults
        {
            MostOffensiveTake = GenerateCategoryWinner("Most Offensive Take", movieEvent.Movie ?? "the movie"),
            HottestTake = GenerateCategoryWinner("Hottest Take", movieEvent.Movie ?? "the movie"),
            BiggestArgumentStarter = GenerateCategoryWinner("Biggest Argument Starter", movieEvent.Movie ?? "the movie"),
            BestJoke = GenerateCategoryWinner("Best Joke", movieEvent.Movie ?? "the movie"),
            BestRoast = GenerateCategoryWinner("Best Roast", movieEvent.Movie ?? "the movie"),
            FunniestRandomTangent = GenerateCategoryWinner("Funniest Random Tangent", movieEvent.Movie ?? "the movie"),
            MostPassionateDefense = GenerateCategoryWinner("Most Passionate Defense", movieEvent.Movie ?? "the movie"),
            BiggestUnanimousReaction = GenerateCategoryWinner("Biggest Unanimous Reaction", movieEvent.Movie ?? "the movie"),
            MostBoringStatement = GenerateCategoryWinner("Most Boring Statement", movieEvent.Movie ?? "the movie"),
            BestPlotTwistRevelation = GenerateCategoryWinner("Best Plot Twist Revelation", movieEvent.Movie ?? "the movie"),
            MovieSnobMoment = GenerateCategoryWinner("Movie Snob Moment", movieEvent.Movie ?? "the movie"),
            GuiltyPleasureAdmission = GenerateCategoryWinner("Guilty Pleasure Admission", movieEvent.Movie ?? "the movie"),
            QuietestPersonBestMoment = GenerateCategoryWinner("Quietest Person's Best Moment", movieEvent.Movie ?? "the movie"),
            AIsUniqueObservations = GenerateTopFiveList("AI's Unique Observations"),
            FunniestSentences = GenerateTopFiveList("Funniest Sentences"),
            MostBlandComments = GenerateTopFiveList("Most Bland Comments")
        };
    }

    private CategoryWinner GenerateCategoryWinner(string category, string movieTitle)
    {
        DemoMember winner = _members[_random.Next(_members.Count)];
        
        return new CategoryWinner
        {
            Speaker = winner.Name,
            Timestamp = TimeSpan.FromMinutes(_random.Next(10, 90)).ToString(),
            Quote = GenerateQuoteForCategory(category, winner, movieTitle),
            Setup = $"During discussion of {movieTitle}",
            GroupReaction = GenerateGroupReaction(),
            WhyItsGreat = GenerateWhyItsGreat(category),
            AudioQuality = AudioQuality.Clear,
            EntertainmentScore = _random.Next(6, 10)
        };
    }

    private TopFiveList GenerateTopFiveList(string category)
    {
        return new TopFiveList
        {
            Entries = Enumerable.Range(1, 5).Select(i => 
                new TopFiveEntry
                {
                    Rank = i,
                    Speaker = _members[_random.Next(_members.Count)].Name,
                    Timestamp = TimeSpan.FromMinutes(_random.Next(10, 90)).ToString(),
                    Quote = GenerateListItem(category),
                    Context = $"During {category.ToLower()} discussion",
                    AudioQuality = AudioQuality.Clear,
                    Score = _random.NextDouble() * 10,
                    Reasoning = $"This ranked #{i} for {category.ToLower()}"
                }
            ).ToList()
        };
    }

    private Dictionary<int, string> GenerateMicAssignments()
    {
        Dictionary<int, string> assignments = new();
        List<DemoMember> attendees = GenerateAttendees();
        
        for (int i = 0; i < attendees.Count; i++)
        {
            assignments[i + 1] = attendees[i].Name;
        }
        
        return assignments;
    }

    private List<AudioFile> GenerateAudioFiles(MovieEvent movieEvent)
    {
        List<DemoMember> attendees = GenerateAttendees();
        List<AudioFile> audioFiles = new();
        
        for (int i = 0; i < attendees.Count; i++)
        {
            audioFiles.Add(new AudioFile
            {
                FileName = $"mic_{i + 1}_{attendees[i].Name.Replace(" ", "_")}.mp3",
                FilePath = $"demo/audio/mic_{i + 1}.mp3",
                FileSize = _random.Next(50_000_000, 200_000_000), // 50-200MB
                Duration = TimeSpan.FromMinutes(_random.Next(60, 120)),
                AudioUrl = $"/demo/audio/mic_{i + 1}.mp3",
                SpeakerNumber = i + 1,
                ProcessingStatus = AudioProcessingStatus.Complete,
                TranscriptText = GenerateTranscriptText(attendees[i]),
                ProgressPercentage = 100,
                CurrentStep = "Complete"
            });
        }
        
        return audioFiles;
    }

    private async Task GenerateAwardsAsync()
    {
        List<Phase> completedPhases = (await _database.GetAllAsync<Phase>())
            .Where(p => p.EndDate <= DateTime.Now)
            .OrderBy(p => p.Number)
            .ToList();

        // Awards happen after every 2 phases (PhasesBeforeAward = 2)
        for (int i = 1; i < completedPhases.Count; i += 2)
        {
            if (i + 1 < completedPhases.Count) // Make sure we have both phases
            {
                Phase phase2 = completedPhases[i]; // The second phase in the pair
                await GeneratePhaseAwardsAsync(phase2);
            }
        }
    }

    private async Task GeneratePhaseAwardsAsync(Phase phase)
    {
        // Create award event
        AwardEvent awardEvent = new AwardEvent
        {
            StartDate = phase.EndDate,
            EndDate = phase.EndDate.AddDays(30),
            VotingStartDate = phase.EndDate.AddDays(7),
            VotingEndDate = phase.EndDate.AddDays(21),
            PhaseNumber = phase.Number
        };
        await _database.UpsertAsync(awardEvent);

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
            await _database.UpsertAsync(question);
            
            awardEvent.Questions.Add(question.Id);
            
            // Generate votes for this category
            await GenerateVotesForCategoryAsync(awardEvent.Id, question.Id, phase.Number);
        }
        
        await _database.UpsertAsync(awardEvent);
    }

    private async Task GenerateVotesForCategoryAsync(Guid awardEventId, Guid questionId, int phaseNumber)
    {
        List<MovieEvent> phaseMovies = (await _database.GetAllAsync<MovieEvent>())
            .Where(me => me.PhaseNumber == phaseNumber)
            .ToList();

        foreach (DemoMember voter in _members)
        {
            if (phaseMovies.Any())
            {
                MovieEvent chosenMovie = phaseMovies[_random.Next(phaseMovies.Count)];
                
                AwardVote vote = new AwardVote
                {
                    AwardEventId = awardEventId,
                    QuestionId = questionId,
                    MovieEventId = chosenMovie.Id,
                    VoterName = voter.Name,
                    VoterIp = $"192.168.1.{_random.Next(1, 255)}",
                    Points = 1
                };
                await _database.UpsertAsync(vote);
            }
        }
    }

    // Helper methods for generating realistic content
    private List<DemoMember> GenerateAttendees()
    {
        // Most sessions have 5-6 attendees out of 7 total
        int attendeeCount = _random.Next(5, 7);
        return _members.OrderBy(x => _random.Next()).Take(attendeeCount).ToList();
    }

    private Dictionary<string, int> GenerateWordCounts(List<DemoMember> attendees)
    {
        return attendees.ToDictionary(
            a => a.Name,
            a => (int)(a.TalkativenessFactor * _random.Next(800, 2000))
        );
    }

    private Dictionary<string, int> GenerateQuestionCounts(List<DemoMember> attendees)
    {
        return attendees.ToDictionary(
            a => a.Name,
            a => _random.Next(2, 12)
        );
    }

    private Dictionary<string, int> GenerateInterruptionCounts(List<DemoMember> attendees)
    {
        return attendees.ToDictionary(
            a => a.Name,
            a => (int)(a.TalkativenessFactor * _random.Next(0, 8))
        );
    }

    private Dictionary<string, int> GenerateLaughterCounts(List<DemoMember> attendees)
    {
        return attendees.ToDictionary(
            a => a.Name,
            a => _random.Next(3, 15)
        );
    }

    private Dictionary<string, int> GenerateCurseWordCounts(List<DemoMember> attendees)
    {
        return attendees.ToDictionary(
            a => a.Name,
            a => _random.Next(0, 5)
        );
    }

    private string GenerateBestMomentsSummary(string movieTitle)
    {
        List<string> templates = new()
        {
            $"The heated debate about {movieTitle}'s ending had everyone talking over each other",
            $"Sofia's emotional reaction to the climax was the highlight of the evening",
            $"Marcus's technical analysis of the cinematography sparked a 20-minute tangent",
            $"David's surprisingly thoughtful take on the horror elements caught everyone off guard",
            $"Jamal's perfectly timed joke during the most serious scene broke the tension"
        };
        
        return templates[_random.Next(templates.Count)];
    }

    private string GenerateConversationTone()
    {
        List<string> tones = new() { "Analytical", "Passionate", "Lighthearted", "Contemplative", "Heated", "Enthusiastic" };
        return tones[_random.Next(tones.Count)];
    }

    private string GenerateQuoteForCategory(string category, DemoMember member, string movieTitle)
    {
        Dictionary<string, List<string>> quotes = new()
        {
            ["Most Offensive Take"] = new() { $"I honestly think {movieTitle} is overrated garbage", "This movie was made for people with no taste", "I've seen student films with better writing" },
            ["Hottest Take"] = new() { $"{movieTitle} is secretly a masterpiece of cinema", "This movie will be studied in film schools for decades", "This is the best film of the decade" },
            ["Best Joke"] = new() { "I've seen more chemistry in my high school lab", "This movie has more plot holes than Swiss cheese", "The acting was so wooden I got splinters" },
            ["Best Roast"] = new() { $"Marcus, your film analysis sounds like you swallowed a textbook", "Sofia, you cry at insurance commercials", "David, not everything needs more blood" }
        };
        
        List<string> categoryQuotes = quotes.GetValueOrDefault(category, new List<string> { $"This is my opinion about {movieTitle}" });
        return categoryQuotes[_random.Next(categoryQuotes.Count)];
    }

    private string GenerateGroupReaction()
    {
        List<string> reactions = new() { "Stunned silence", "Burst of laughter", "Collective gasp", "Heated argument", "Nodding agreement", "Confused murmurs" };
        return reactions[_random.Next(reactions.Count)];
    }

    private string GenerateWhyItsGreat(string category)
    {
        return $"This perfectly captures what makes a great {category.ToLower()} - it's unexpected, memorable, and sparked genuine discussion among the group.";
    }

    private string GenerateListItem(string category)
    {
        Dictionary<string, List<string>> items = new()
        {
            ["AI's Unique Observations"] = new() { "The color palette subtly shifts with character development", "Background music mirrors the protagonist's emotional state", "Lighting changes reflect internal conflicts" },
            ["Funniest Sentences"] = new() { "I laughed so hard I snorted wine through my nose", "That plot twist hit me like a truck full of confusion", "I have questions about the physics of that action scene" },
            ["Most Bland Comments"] = new() { "It was okay I guess", "The movie happened and then it ended", "I've seen worse films this year" }
        };
        
        List<string> categoryItems = items.GetValueOrDefault(category, new List<string> { "Generic comment about the movie" });
        return categoryItems[_random.Next(categoryItems.Count)];
    }

    private string GenerateTranscriptText(DemoMember member)
    {
        List<string> phrases = new()
        {
            $"I think {member.Quirks.ToLower()}",
            $"From my perspective as someone who {member.Personality.ToLower()}",
            "What really struck me about this film was",
            "I have to disagree with that assessment",
            "That's an interesting point, but consider this"
        };
        
        return string.Join(". ", phrases.Take(_random.Next(2, 4))) + ".";
    }

    private async Task CreateAwardEventForMonth(DateTime awardMonth, int completedPhaseNumber)
    {
        Console.WriteLine($"   🏆 Creating award event for {awardMonth:yyyy-MM}");
        
        AwardEvent awardEvent = new AwardEvent
        {
            StartDate = awardMonth,
            EndDate = awardMonth.AddMonths(1).AddDays(-1),
            VotingStartDate = awardMonth.AddDays(7),  // Voting starts 1 week into the month
            VotingEndDate = awardMonth.AddDays(21),   // Voting ends 3 weeks into the month
            PhaseNumber = completedPhaseNumber
        };
        await _database.UpsertAsync(awardEvent);

        // Create award categories/questions
        List<string> categories = new()
        {
            "Best Overall Film",
            "Most Surprising Discovery", 
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
            await _database.UpsertAsync(question);
            
            awardEvent.Questions.Add(question.Id);
        }
        
        await _database.UpsertAsync(awardEvent);
        
        // Generate votes for past award events (not future ones)
        if (awardMonth < DateTime.Now)
        {
            foreach (Guid questionId in awardEvent.Questions)
            {
                await GenerateVotesForCategoryAsync(awardEvent.Id, questionId, completedPhaseNumber);
            }
        }
    }

    private class DemoMember
    {
        public string Name { get; set; } = string.Empty;
        public int AgeAtStart { get; set; }
        public string Personality { get; set; } = string.Empty;
        public string[] Preferences { get; set; } = Array.Empty<string>();
        public string Quirks { get; set; } = string.Empty;
        public string Snack { get; set; } = string.Empty;
        public double TalkativenessFactor { get; set; }
        public double CriticalnessFactor { get; set; }
    }
}