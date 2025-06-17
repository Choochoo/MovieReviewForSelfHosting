using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Application.Services.Analysis;
using MovieReviewApp.Application.Services.Processing;
using MovieReviewApp.Components;
using MovieReviewApp.Controllers;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Infrastructure.Database;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Middleware;
using MovieReviewApp.Models;
using MovieReviewApp.Utilities;

// Configure MongoDB serialization
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));

// Configure MongoDB to ignore extra elements (for properties that were removed or have [BsonIgnore])
ConventionPack conventionPack = new ConventionPack { new IgnoreExtraElementsConvention(true) };
ConventionRegistry.Register("IgnoreExtraElements", conventionPack, type => true);


// Parse command line arguments
CommandLineArgs cmdArgs = CommandLineParser.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());

// Handle special commands
if (cmdArgs.ShowHelp)
{
    CommandLineParser.ShowHelp();
    return;
}

if (cmdArgs.ListInstances)
{
    CommandLineParser.ListInstances();
    return;
}

if (cmdArgs.GenerateDemo)
{
    await GenerateDemoDataAsync();
    return;
}




// Initialize instance manager with command line instance name or default to "Default"
string? instanceName = cmdArgs.InstanceName;
if (string.IsNullOrEmpty(instanceName))
{
    instanceName = "Default";
}

InstanceManager instanceManager = new InstanceManager(instanceName);
Console.WriteLine($"Starting Movie Review App instance: {instanceManager.InstanceName}");

// Get instance configuration
InstanceConfig instanceConfig = instanceManager.GetInstanceConfig();

// Override port if specified in command line
if (cmdArgs.Port.HasValue)
{
    instanceConfig.Port = cmdArgs.Port.Value;
    instanceManager.SaveInstanceConfig(instanceConfig);
}

// Update last used timestamp
instanceConfig.LastUsed = DateTime.UtcNow;
instanceManager.SaveInstanceConfig(instanceConfig);

WebApplicationBuilder builder = WebApplication.CreateBuilder(Environment.GetCommandLineArgs());

// Set the port from instance configuration
builder.WebHost.UseUrls($"http://localhost:{instanceConfig.Port}");

// Initialize demo protection and secure configuration
DemoProtectionService demoProtection = new DemoProtectionService(instanceManager);
SecretsManager secretsManager = new SecretsManager(instanceManager, demoProtection);
builder.Services.AddSingleton(instanceManager);
builder.Services.AddSingleton(demoProtection);
builder.Services.AddSingleton(secretsManager);

// Add secure configuration provider
((IConfigurationBuilder)builder.Configuration).Add(new SecureConfigurationSource(secretsManager));

// Use base configuration (no more separate Adult/Kid configs)
string configFile = "appsettings.json";
string templateFile = "appsettings.json.template";

// Use template file if main config doesn't exist (for public distribution)  
if (!File.Exists(configFile) && File.Exists(templateFile))
{
    _ = builder.Configuration.AddJsonFile(templateFile, optional: true, reloadOnChange: true);
}
else
{
    _ = builder.Configuration.AddJsonFile(configFile, optional: true, reloadOnChange: true);
}

// Define and configure AppSettings
builder.Services.Configure<AppSettings>(options =>
{
    builder.Configuration.GetSection("AppSettings").Bind(options);
    options.ContentType = instanceConfig.Environment;
    options.Title = instanceConfig.DisplayName;
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure SignalR for larger messages (for image uploads)
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 50 * 1024 * 1024; // 50MB
});

builder.Services.AddControllers();


builder.Services.AddHttpClient(); // Register HttpClient

builder.Services.AddHttpContextAccessor();

// Configure file upload size limit (10GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024; // 10 GB
});

// Register database service
builder.Services.AddSingleton<MongoDbService>();

// Register model services
builder.Services.AddScoped<AwardEventService>();
builder.Services.AddScoped<AwardQuestionService>();
builder.Services.AddScoped<AwardVoteService>();
builder.Services.AddScoped<ImageStorageService>();
builder.Services.AddScoped<MovieEventService>();
builder.Services.AddScoped<PersonService>();
builder.Services.AddScoped<PhaseService>();
builder.Services.AddScoped<SettingService>();
builder.Services.AddScoped<SiteUpdateService>();
builder.Services.AddScoped<SoundClipService>();

builder.Services.AddScoped<MovieReviewService>();

// MongoDb service replaced with generic system
builder.Services.AddScoped<MessengerService>();
builder.Services.AddScoped<ImageService>();
builder.Services.AddScoped<MarkdownService>();


// Register new audio processing services
builder.Services.AddHttpClient<GladiaService>(client =>
{
    client.BaseAddress = new Uri("https://api.gladia.io");
    client.Timeout = TimeSpan.FromHours(1); // Long timeout for audio uploads
});
builder.Services.AddScoped<ClaudeService>();
builder.Services.AddScoped<PromptService>();

// Register refactored analysis services
builder.Services.AddScoped<WordAnalysisService>();
builder.Services.AddScoped<TranscriptProcessingService>();
builder.Services.AddScoped<PromptGenerationService>();
builder.Services.AddScoped<ResponseParsingService>();
builder.Services.AddScoped<SimpleSessionStatsService>();
builder.Services.AddScoped<SpeakerAttributionFixService>();
builder.Services.AddHttpClient<OpenAIApiService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout for OpenAI requests
});
builder.Services.AddScoped<OpenAIApiService>();

// Register audio processing services
builder.Services.AddScoped<AudioProcessingService>();
builder.Services.AddScoped<AudioProcessingStateMachine>();
builder.Services.AddScoped<MovieSessionService>();
builder.Services.AddScoped<AudioClipService>();
builder.Services.AddScoped<AudioFileOrganizer>();
builder.Services.AddScoped<DiscussionQuestionService>();
builder.Services.AddScoped<ThemeService>();

// Analysis Services
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<OpenAIAnalysisService>();
builder.Services.AddScoped<TranscriptProcessingService>();

// Background Services
builder.Services.AddHostedService<MovieReviewApp.Application.Services.MonthlyDataGenerationService>();


// Processing Services
builder.Services.AddScoped<MovieReviewApp.Application.Services.Processing.FileProcessingService>();
builder.Services.AddScoped<AudioProcessingStateMachine>();
builder.Services.AddScoped<FileUploadService>();

// Configure Facebook settings from secure config  
builder.Services.Configure<FacebookSettings>(options =>
{
    string chatUrl = secretsManager.GetSecret("Facebook:ChatUrl");

    if (!string.IsNullOrEmpty(chatUrl))
    {
        options.ChatUrl = chatUrl;
    }
});

// Configure Kestrel
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024; // 10 GB
});

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
});

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
}


// Add first-run setup middleware (before static files for setup page styling)
app.UseFirstRunSetup();

// Configure static files with proper MIME types for audio
FileExtensionContentTypeProvider provider = new FileExtensionContentTypeProvider();
provider.Mappings[".mp3"] = "audio/mpeg";
provider.Mappings[".wav"] = "audio/wav";
provider.Mappings[".ogg"] = "audio/ogg";
provider.Mappings[".m4a"] = "audio/mp4";
provider.Mappings[".aac"] = "audio/aac";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider,
    OnPrepareResponse = ctx =>
    {
        // Add headers for audio files
        if (ctx.File.Name.EndsWith(".mp3") || ctx.File.Name.EndsWith(".wav") ||
            ctx.File.Name.EndsWith(".ogg") || ctx.File.Name.EndsWith(".m4a") ||
            ctx.File.Name.EndsWith(".aac"))
        {
            ctx.Context.Response.Headers.Append("Accept-Ranges", "bytes");
            ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=3600");
        }
    }
});
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

// Display instance information
Console.WriteLine();
Console.WriteLine("🎬 Movie Review App Instance Started!");
Console.WriteLine($"   Instance: {instanceManager.InstanceName}");
Console.WriteLine($"   Display Name: {instanceConfig.DisplayName}");
Console.WriteLine($"   Content Type: {instanceConfig.Environment}");
Console.WriteLine($"   Port: {instanceConfig.Port}");
Console.WriteLine($"   URL: http://localhost:{instanceConfig.Port}");
Console.WriteLine();
Console.WriteLine("Press Ctrl+C to stop the application");
Console.WriteLine();


app.Run();

async Task GenerateDemoDataAsync()
{
    Console.WriteLine("🎬 Generating Movie Review App Demo Data...");
    Console.WriteLine();
    
    try
    {
        // Initialize demo instance manager
        InstanceManager demoInstanceManager = new InstanceManager("demo");
        Console.WriteLine("Setting up demo instance configuration...");
        
        // Configure demo instance
        InstanceConfig demoConfig = demoInstanceManager.GetInstanceConfig();
        demoConfig.DisplayName = "Demo Movie Review Club";
        demoConfig.Description = "Demonstration instance with 10 years of generated data";
        demoConfig.Environment = "Demo";
        demoConfig.Port = 5555;
        demoInstanceManager.SaveInstanceConfig(demoConfig);
        
        // Initialize secure configuration for demo
        DemoProtectionService demoDemoProtection = new DemoProtectionService(demoInstanceManager);
        SecretsManager demoSecretsManager = new SecretsManager(demoInstanceManager, demoDemoProtection);
        
        // Use your existing MongoDB Atlas connection for demo
        string mongoConnection = "mongodb+srv://jaredbrowne:CjjTcmNP92xiL3we@moviereviewcluster.oi47y0a.mongodb.net/";
        Console.WriteLine($"📋 Using MongoDB Atlas for demo data");
        Console.WriteLine($"   Database: moviereview_demo_demo");
        Console.WriteLine();
        
        // Set up MongoDB connection
        demoSecretsManager.SetSecret("MongoDB:ConnectionString", mongoConnection);
        
        // Create configuration builder for demo instance
        WebApplicationBuilder demoBuilder = WebApplication.CreateBuilder();
        
        // Add secure configuration
        ((IConfigurationBuilder)demoBuilder.Configuration).Add(new SecureConfigurationSource(demoSecretsManager));
        
        // Register core services needed for demo data generation
        demoBuilder.Services.AddSingleton(demoInstanceManager);
        demoBuilder.Services.AddSingleton(demoSecretsManager);
        demoBuilder.Services.AddSingleton<MongoDbService>();
        demoBuilder.Services.AddHttpClient<MovieReviewApp.Application.Services.TmdbService>();
        demoBuilder.Services.AddScoped<MovieReviewApp.Application.Services.TmdbService>();
        demoBuilder.Services.AddScoped<MovieReviewApp.Infrastructure.FileSystem.ImageService>();
        demoBuilder.Services.AddScoped<MovieReviewApp.Application.Services.DemoDataService>();
        
        // Build the demo app container
        WebApplication demoApp = demoBuilder.Build();
        
        // Generate demo data
        using (IServiceScope scope = demoApp.Services.CreateScope())
        {
            MovieReviewApp.Application.Services.DemoDataService demoDataService = scope.ServiceProvider.GetRequiredService<MovieReviewApp.Application.Services.DemoDataService>();
            
            Console.WriteLine("Generating 2 years of movie review club data...");
            Console.WriteLine("- Creating 7 consistent AI members");
            Console.WriteLine("- Fetching real movie data from TMDB");
            Console.WriteLine("- Downloading and storing movie posters");
            Console.WriteLine("- Generating phases and movie selections with real data");
            Console.WriteLine("- Creating realistic discussion sessions");
            Console.WriteLine("- Building award voting history");
            Console.WriteLine();
            
            await demoDataService.GenerateDemoDataAsync();
        }
        
        Console.WriteLine("✅ Demo data generation complete!");
        Console.WriteLine();
        Console.WriteLine("🚀 To run the demo instance:");
        Console.WriteLine("   dotnet run --instance demo --port 5555");
        Console.WriteLine();
        Console.WriteLine("🌐 Demo will be available at:");
        Console.WriteLine("   http://localhost:5555");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error generating demo data: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}
