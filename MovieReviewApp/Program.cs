using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MovieReviewApp.Components;
using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Database;
using MovieReviewApp.Middleware;
using MovieReviewApp.Models;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Application.Services.Analysis;
using MovieReviewApp.Application.Services.Session;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Infrastructure.FileSystem;
using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Infrastructure.Services;
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

// Initialize secure configuration
SecretsManager secretsManager = new SecretsManager(instanceManager);
builder.Services.AddSingleton(instanceManager);
builder.Services.AddSingleton(secretsManager);

// Add secure configuration provider
((IConfigurationBuilder)builder.Configuration).Add(new SecureConfigurationSource(secretsManager));

// Use base configuration (no more separate Adult/Kid configs)
string configFile = "appsettings.json";
string templateFile = "appsettings.json.template";

// Use template file if main config doesn't exist (for public distribution)  
if (!File.Exists(configFile) && File.Exists(templateFile))
{
    builder.Configuration.AddJsonFile(templateFile, optional: true, reloadOnChange: true);
}
else
{
    builder.Configuration.AddJsonFile(configFile, optional: true, reloadOnChange: true);
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

// Configure HttpClient for GladiaService with extended timeout for large file uploads
builder.Services.AddHttpClient<GladiaService>(client =>
{
    client.Timeout = TimeSpan.FromHours(1); // 1 hour timeout for large uploads and processing
});
builder.Services.AddHttpContextAccessor();

// Configure file upload size limit (10GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024; // 10 GB
});

// Register database service
builder.Services.AddSingleton<IDatabaseService, MongoDbService>();

// Register award services
builder.Services.AddScoped<IAwardEventService, AwardEventRepository>();
builder.Services.AddScoped<IAwardVoteService, AwardVoteRepository>();

builder.Services.AddScoped<MovieReviewService>();

// MongoDb service replaced with generic system
builder.Services.AddScoped<MessengerService>();
builder.Services.AddScoped<ImageService>();
builder.Services.AddScoped<MarkdownService>();


// Register new audio processing services
builder.Services.AddScoped<GladiaService>();
builder.Services.AddScoped<OpenAIService>();
builder.Services.AddScoped<ClaudeService>();
builder.Services.AddScoped<PromptService>();

// Register refactored analysis services
builder.Services.AddScoped<TranscriptProcessingService>();
builder.Services.AddScoped<PromptGenerationService>();
builder.Services.AddScoped<ResponseParsingService>();
builder.Services.AddScoped<SimpleSessionStatsService>();
builder.Services.AddScoped<SpeakerAttributionFixService>();
builder.Services.AddHttpClient<OpenAIApiService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(20); // 20 minute timeout for OpenAI analysis
});
builder.Services.AddScoped<MovieSessionAnalysisOrchestrator>();

// Register refactored session services
builder.Services.AddScoped<SessionRepositoryService>();
builder.Services.AddScoped<SessionMetadataService>();
builder.Services.AddScoped<AudioProcessingWorkflowService>();
builder.Services.AddScoped<SessionAnalysisService>();
builder.Services.AddScoped<SessionMaintenanceService>();
builder.Services.AddScoped<SessionOrchestrationService>();

// Keep the original MovieSessionAnalysisService for backward compatibility
builder.Services.AddScoped<MovieSessionAnalysisService>();
builder.Services.AddScoped<MovieSessionService>();
builder.Services.AddScoped<AudioClipService>();
builder.Services.AddScoped<AudioFileOrganizer>();
builder.Services.AddScoped<DiscussionQuestionsService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<SoundboardRepository>();

// MongoDB connection is now handled directly in the MongoDb constructor using instance secrets

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
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
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