using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using MovieReviewApp.Components;
using MovieReviewApp.Components.Layout;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient(); // Register HttpClient

var app = builder.Build();

// Middleware to redirect HTTPS to HTTP
app.Use(async (context, next) =>
{
    if (context.Request.IsHttps)
    {
        var httpUrl = $"http://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(httpUrl);
    }
    else
    {
        await next();
    }
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
