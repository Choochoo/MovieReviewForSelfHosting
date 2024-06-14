using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using MovieReviewApp.Components;
using System;

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
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000); // HTTP port
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps("C:\\localhost.pfx", certPassword);
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Enable HTTPS redirection
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
