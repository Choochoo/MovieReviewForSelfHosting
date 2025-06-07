namespace MovieReviewApp.Extentions
{
    public static class WebHostEnvironmentExtensions
    {
        public static string ContentType(this IWebHostEnvironment env)
        {
            // This method is deprecated in favor of instance-based configuration
            // Instance content type is now managed by InstanceManager
            return "General"; // Default to "General" content type
        }
    }
}
