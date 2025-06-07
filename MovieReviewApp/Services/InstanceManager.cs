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
            // Check for custom data path from environment variable
            var customDataPath = Environment.GetEnvironmentVariable("MOVIEREVIEW_DATA_PATH");
            string basePath;
            
            if (!string.IsNullOrEmpty(customDataPath))
            {
                // Use custom path if provided
                basePath = customDataPath;
                Console.WriteLine($"InstanceManager: Using custom data path from environment: {basePath}");
            }
            else if (OperatingSystem.IsWindows() || IsRunningInWSL())
            {
                // Windows or WSL: Use ProgramData for easier server access
                if (IsRunningInWSL())
                {
                    // In WSL, use Windows ProgramData path
                    basePath = "/mnt/c/ProgramData";
                }
                else
                {
                    basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                }
            }
            else
            {
                // Linux/Unix: Use /etc or /var/lib for server-friendly access
                if (Directory.Exists("/etc") && HasWritePermission("/etc"))
                {
                    basePath = "/etc";
                }
                else if (Directory.Exists("/var/lib") && HasWritePermission("/var/lib"))
                {
                    basePath = "/var/lib";
                }
                else
                {
                    // Fallback to user's home directory
                    basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
            }
            
            _instancesRootPath = Path.Combine(basePath, "MovieReviewApp", "instances");
            
            _instanceName = instanceName ?? GetDefaultInstanceName();
            _instancePath = Path.Combine(_instancesRootPath, _instanceName);
            
            Console.WriteLine($"InstanceManager: Running in WSL: {IsRunningInWSL()}");
            Console.WriteLine($"InstanceManager: Base path: {basePath}");
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

        private static bool HasWritePermission(string path)
        {
            try
            {
                // Try to create a test directory to check write permissions
                var testPath = Path.Combine(path, ".moviereview_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(testPath);
                Directory.Delete(testPath);
                return true;
            }
            catch
            {
                return false;
            }
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