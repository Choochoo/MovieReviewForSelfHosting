using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using MovieReviewApp.Components;
using MovieReviewApp.Database;
using MovieReviewApp.Extentions;
using MovieReviewApp.Models;

var builder = WebApplication.CreateBuilder(args);

// Add configuration sources
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.Audience()}.json", optional: true, reloadOnChange: true);

// Define and configure AppSettings
builder.Services.Configure<AppSettings>(options =>
{
    builder.Configuration.GetSection("AppSettings").Bind(options);
    options.IsKid = builder.Environment.EnvironmentName.Equals("Kid", StringComparison.OrdinalIgnoreCase);
    options.Title = options.IsKid ? "Browne Filmatorium" : "Film Schmilm Club";
});


// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient(); // Register HttpClient

// Configure file upload size limit (10GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024; // 10 GB
});

builder.Services.AddSingleton<MongoDb>();

// Configure Kestrel
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 10L * 1024 * 1024 * 1024; // 10 GB
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();