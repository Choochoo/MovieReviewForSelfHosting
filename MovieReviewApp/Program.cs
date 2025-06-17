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

