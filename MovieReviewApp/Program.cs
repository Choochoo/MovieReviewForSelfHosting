using Microsoft.AspNetCore.Http.Features;
using MovieReviewApp.Components;
using MovieReviewApp.Database;
using MovieReviewApp.Middleware;
using MovieReviewApp.Models;
using MovieReviewApp.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

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
var instanceName = cmdArgs.InstanceName;
if (string.IsNullOrEmpty(instanceName))
{
    instanceName = "Default";
}

var instanceManager = new InstanceManager(instanceName);
Console.WriteLine($"Starting Movie Review App instance: {instanceManager.InstanceName}");

// Get instance configuration
var instanceConfig = instanceManager.GetInstanceConfig();

// Override port if specified in command line
if (cmdArgs.Port.HasValue)
{
    instanceConfig.Port = cmdArgs.Port.Value;
    instanceManager.SaveInstanceConfig(instanceConfig);
}

// Update last used timestamp
instanceConfig.LastUsed = DateTime.UtcNow;
instanceManager.SaveInstanceConfig(instanceConfig);

var builder = WebApplication.CreateBuilder(Environment.GetCommandLineArgs());

// Set the port from instance configuration
builder.WebHost.UseUrls($"http://localhost:{instanceConfig.Port}");

// Initialize secure configuration
var secretsManager = new SecretsManager(instanceManager);
builder.Services.AddSingleton(instanceManager);
builder.Services.AddSingleton(secretsManager);

// Add secure configuration provider
((IConfigurationBuilder)builder.Configuration).Add(new SecureConfigurationSource(secretsManager));

// Use base configuration (no more separate Adult/Kid configs)
var configFile = "appsettings.json";
var templateFile = "appsettings.json.template";

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
builder.Services.AddHttpContextAccessor();

// Configure file upload size limit (10GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024; // 10 GB
});

// Add MongoDB service
builder.Services.AddSingleton<MongoDbService>(provider => 
    new MongoDbService(
        provider.GetRequiredService<IConfiguration>(),
        provider.GetRequiredService<SecretsManager>(),
        provider.GetRequiredService<InstanceManager>(),
        provider.GetRequiredService<ILogger<MongoDbService>>()));

builder.Services.AddScoped<MovieReviewService>();

// MongoDb service replaced with generic system
builder.Services.AddScoped<MessengerService>();
builder.Services.AddScoped<ImageService>();
builder.Services.AddScoped<MarkdownService>();

// Only register stats processor if not in first-run setup (it requires database)
if (!instanceManager.IsFirstRun)
{
    builder.Services.AddScoped<StatsCommandProcessorService>(provider =>
        new StatsCommandProcessorService(
            provider.GetRequiredService<IWebHostEnvironment>().WebRootPath,
            provider.GetRequiredService<MovieReviewService>()));
}

// Register new audio processing services
builder.Services.AddScoped<GladiaService>();
builder.Services.AddScoped<MovieSessionAnalysisService>();
builder.Services.AddScoped<MovieSessionService>();
builder.Services.AddScoped<AudioClipService>();
builder.Services.AddScoped<AudioFileOrganizer>();

// MongoDB connection is now handled directly in the MongoDb constructor using instance secrets

// Configure Facebook settings from secure config  
builder.Services.Configure<FacebookSettings>(options =>
{
    var chatUrl = secretsManager.GetSecret("Facebook:ChatUrl");

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Add first-run setup middleware (before static files for setup page styling)
app.UseFirstRunSetup();

app.UseStaticFiles();
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

BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.CSharpLegacy));

app.Run();