using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Middleware
{
    public class FirstRunSetupMiddleware
    {
        private readonly RequestDelegate _next;

        public FirstRunSetupMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, SecretsManager secretsManager, InstanceManager instanceManager)
        {
            // Skip setup check for setup page itself and static files
            string? path = context.Request.Path.Value?.ToLower();
            
            if (path != null && (
                path.StartsWith("/setup") ||
                path.StartsWith("/_framework") ||
                path.StartsWith("/_blazor") ||
                path.StartsWith("/css") ||
                path.StartsWith("/js") ||
                path.StartsWith("/images") ||
                path.StartsWith("/favicon") ||
                path.Contains("."))) // Skip file requests
            {
                await _next(context);
                return;
            }

            // Only redirect if we're not already on setup page and missing required config
            if (path != "/setup")
            {
                // Check if this is first run
                if (secretsManager.IsFirstRun)
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = 302;
                        context.Response.Headers.Location = "/setup";
                        await context.Response.CompleteAsync();
                        return;
                    }
                }

                // Check if required secrets are missing
                if (!secretsManager.HasRequiredSecrets())
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = 302;
                        context.Response.Headers.Location = "/setup";
                        await context.Response.CompleteAsync();
                        return;
                    }
                }
            }

            await _next(context);
        }
    }

    public static class FirstRunSetupMiddlewareExtensions
    {
        public static IApplicationBuilder UseFirstRunSetup(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FirstRunSetupMiddleware>();
        }
    }
}