namespace MovieReviewApp.Extentions
{
    public static class WebHostEnvironmentExtensions
    {
        public static string Audience(this IWebHostEnvironment env)
        {
            // This will work with launchsettings.json environmentVariables
            var audience = Environment.GetEnvironmentVariable("AUDIENCE");
            return audience ?? "Adult"; // Default to "Adult" if not set
        }
    }
}
