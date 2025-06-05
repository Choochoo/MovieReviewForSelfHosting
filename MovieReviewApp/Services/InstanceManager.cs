using System.Text.Json;

namespace MovieReviewApp.Services
{
    public class InstanceManager
    {
        private readonly string _instancesRootPath;
        private readonly string _instanceName;
        private readonly string _instancePath;

        public InstanceManager(string? instanceName = null)
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            // Handle WSL environment - use Windows path if running in WSL
            if (IsRunningInWSL())
            {
                // In WSL, we need to use the Windows AppData path
                // Try to detect the Windows username from the current path or use hardcoded fallback
                var currentPath = Directory.GetCurrentDirectory();
                Console.WriteLine($"InstanceManager: Current directory in WSL: {currentPath}");
                
                // Extract Windows username from path if possible
                var match = System.Text.RegularExpressions.Regex.Match(currentPath, @"/mnt/c/Users/([^/]+)/");
                if (match.Success)
                {
                    var windowsUsername = match.Groups[1].Value;
                    appDataPath = $"/mnt/c/Users/{windowsUsername}/AppData/Roaming";
                    Console.WriteLine($"InstanceManager: Detected Windows username: {windowsUsername}");
                }
                else
                {
                    // Fallback to hardcoded path
                    appDataPath = "/mnt/c/Users/Jared/AppData/Roaming";
                    Console.WriteLine($"InstanceManager: Using fallback Windows username: Jared");
                }
            }
            
            _instancesRootPath = Path.Combine(appDataPath, "MovieReviewApp", "instances");
            
            _instanceName = instanceName ?? GetDefaultInstanceName();
            _instancePath = Path.Combine(_instancesRootPath, _instanceName);
            
            Console.WriteLine($"InstanceManager: Running in WSL: {IsRunningInWSL()}");
            Console.WriteLine($"InstanceManager: AppData path: {appDataPath}");
            Console.WriteLine($"InstanceManager: Instance path: {_instancePath}");
            Console.WriteLine($"InstanceManager: Secrets path: {SecretsPath}");
            Console.WriteLine($"InstanceManager: Config path: {ConfigPath}");
            
            Directory.CreateDirectory(_instancePath);
        }

        public string InstanceName => _instanceName;
        public string InstancePath => _instancePath;
        public string SecretsPath => Path.Combine(_instancePath, "secrets.json");
        public string ConfigPath => Path.Combine(_instancePath, "config.json");

        public bool IsFirstRun => !File.Exists(SecretsPath) || !File.Exists(ConfigPath);

        public List<string> GetAllInstances()
        {
            if (!Directory.Exists(_instancesRootPath))
                return new List<string>();

            return Directory.GetDirectories(_instancesRootPath)
                           .Select(Path.GetFileName)
                           .Where(name => !string.IsNullOrEmpty(name))
                           .ToList()!;
        }

        public InstanceConfig GetInstanceConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                return new InstanceConfig
                {
                    InstanceName = _instanceName,
                    Port = GetDefaultPort(),
                    Environment = "General",
                    DisplayName = _instanceName,
                    CreatedDate = DateTime.UtcNow
                };
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<InstanceConfig>(json) ?? new InstanceConfig
                {
                    InstanceName = _instanceName,
                    Port = GetDefaultPort(),
                    Environment = "General",
                    DisplayName = _instanceName,
                    CreatedDate = DateTime.UtcNow
                };
            }
            catch
            {
                return new InstanceConfig
                {
                    InstanceName = _instanceName,
                    Port = GetDefaultPort(),
                    Environment = "General", 
                    DisplayName = _instanceName,
                    CreatedDate = DateTime.UtcNow
                };
            }
        }

        public void SaveInstanceConfig(InstanceConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save instance config: {ex.Message}", ex);
            }
        }

        public static string SanitizeInstanceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Default";

            // Remove invalid filename characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
            
            // Replace spaces with hyphens
            sanitized = sanitized.Replace(' ', '-');
            
            // Ensure it's not empty after sanitization
            return string.IsNullOrEmpty(sanitized) ? "Default" : sanitized;
        }

        private string GetDefaultInstanceName()
        {
            // Try to detect from environment or use default
            var envInstance = Environment.GetEnvironmentVariable("MOVIEREVIEW_INSTANCE");
            if (!string.IsNullOrEmpty(envInstance))
                return SanitizeInstanceName(envInstance);

            return "Default";
        }

        private int GetDefaultPort()
        {
            // Try to get port from environment variable or command line
            var envPort = Environment.GetEnvironmentVariable("MOVIEREVIEW_PORT");
            if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var port))
                return port;

            // Generate unique port based on instance name hash
            return 5000 + Math.Abs(_instanceName.GetHashCode() % 100);
        }

        private static bool IsRunningInWSL()
        {
            // Check if we're running in WSL by looking for WSL-specific environment variables
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_INTEROP")) ||
                   File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop");
        }
    }

    public class InstanceConfig
    {
        public string InstanceName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Port { get; set; } = 5000;
        public string Environment { get; set; } = "General"; // "General" or "Family"
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public string Description { get; set; } = "";
    }
}