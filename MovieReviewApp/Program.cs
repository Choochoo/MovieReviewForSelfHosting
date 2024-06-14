using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using MovieReviewApp.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Get the certificate password from environment variables
var certPassword = Environment.GetEnvironmentVariable("CertPassword");

if (string.IsNullOrEmpty(certPassword))
{
    throw new InvalidOperationException("Certificate password is not set in environment variables.");
}

// Configure Kestrel to use the HTTPS certificate
builder.WebHost.ConfigureKestrel((context, options) =>
{
    var kestrelSection = context.Configuration.GetSection("Kestrel");

    // Configure HTTP endpoint
    var httpEndpoint = kestrelSection.GetSection("Endpoints:Http:Url").Value;
    if (!string.IsNullOrEmpty(httpEndpoint))
    {
        options.ListenAnyIP(new Uri(httpEndpoint).Port);
    }

    // Configure HTTPS endpoint with the certificate password from environment variables
    var httpsEndpoint = kestrelSection.GetSection("Endpoints:Https:Url").Value;
    var certPath = kestrelSection.GetSection("Endpoints:Https:Certificate:Path").Value;
    if (!string.IsNullOrEmpty(httpsEndpoint) && !string.IsNullOrEmpty(certPath))
    {
        options.ListenAnyIP(new Uri(httpsEndpoint).Port, listenOptions =>
        {
            listenOptions.UseHttps(certPath, certPassword);
        });
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Enable HTTPS redirection
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
