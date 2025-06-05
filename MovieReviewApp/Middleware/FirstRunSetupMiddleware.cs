using MovieReviewApp.Services;

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
            var path = context.Request.Path.Value?.ToLower();
            Console.WriteLine($"FirstRunSetupMiddleware: Processing path: {path}");
            
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
                Console.WriteLine($"FirstRunSetupMiddleware: Checking IsFirstRun...");
                // Check if this is first run
                if (secretsManager.IsFirstRun)
                {
                    Console.WriteLine($"FirstRunSetupMiddleware: IsFirstRun=true for path {path}");
                    Console.WriteLine($"FirstRunSetupMiddleware: Checking file existence:");
                    Console.WriteLine($"  Secrets file: {instanceManager.SecretsPath} - Exists: {File.Exists(instanceManager.SecretsPath)}");
                    Console.WriteLine($"  Config file: {instanceManager.ConfigPath} - Exists: {File.Exists(instanceManager.ConfigPath)}");
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Redirect("/setup");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"FirstRunSetupMiddleware: IsFirstRun=false");
                }

                // Check if required secrets are missing
                Console.WriteLine($"FirstRunSetupMiddleware: Checking HasRequiredSecrets...");
                if (!secretsManager.HasRequiredSecrets())
                {
                    Console.WriteLine($"FirstRunSetupMiddleware: Missing required secrets for path {path}");
                    Console.WriteLine($"  Missing: {string.Join(", ", secretsManager.GetMissingSecrets())}");
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Redirect("/setup");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"FirstRunSetupMiddleware: All required secrets present");
                }
            }

            Console.WriteLine($"FirstRunSetupMiddleware: Passing to next middleware for path {path}");
            await _next(context);
            Console.WriteLine($"FirstRunSetupMiddleware: Completed processing for path {path}");
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