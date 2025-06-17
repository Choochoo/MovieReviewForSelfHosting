using System.Text.Json;
using MovieReviewApp.Infrastructure.Services;

namespace MovieReviewApp.Infrastructure.Configuration
{
    public class SecretsManager
    {
        private readonly string _secretsFilePath;
        private readonly InstanceManager _instanceManager;
        private readonly DemoProtectionService _demoProtection;
        private Dictionary<string, string> _secrets;

        public SecretsManager(InstanceManager instanceManager, DemoProtectionService demoProtection)
        {
            _instanceManager = instanceManager;
            _demoProtection = demoProtection;
            _secretsFilePath = instanceManager.SecretsPath;
            _secrets = LoadSecrets();
        }

        public bool IsFirstRun => _instanceManager.IsFirstRun;

        public string? GetSecret(string key)
        {
            return _secrets.TryGetValue(key, out var value) ? value : null;
        }

        public void SetSecret(string key, string value)
        {
            _demoProtection.ValidateNotDemo("Save API keys or secrets");
            
            _secrets[key] = value;
            SaveSecrets();
        }

        public void SetSecrets(Dictionary<string, string> secrets)
        {
            _demoProtection.ValidateNotDemo("Save API keys or secrets");
            
            foreach (var kvp in secrets)
            {
                _secrets[kvp.Key] = kvp.Value;
            }
            SaveSecrets();
        }

        public bool HasRequiredSecrets()
        {
            string[] requiredKeys = new[]
            {
                "TMDB:ApiKey",
                "MongoDB:ConnectionString"
            };

            return requiredKeys.All(key => !string.IsNullOrEmpty(GetSecret(key)));
        }

        public List<string> GetMissingSecrets()
        {
            string[] requiredKeys = new[]
            {
                "TMDB:ApiKey",
                "MongoDB:ConnectionString"
            };

            return requiredKeys.Where(key => string.IsNullOrEmpty(GetSecret(key))).ToList();
        }

        private Dictionary<string, string> LoadSecrets()
        {
            Console.WriteLine($"SecretsManager.LoadSecrets: Checking file existence at {_secretsFilePath}");
            if (!File.Exists(_secretsFilePath))
            {
                Console.WriteLine($"SecretsManager.LoadSecrets: File does not exist");
                return new Dictionary<string, string>();
            }

            try
            {
                Console.WriteLine($"SecretsManager.LoadSecrets: Reading file");
                string json = File.ReadAllText(_secretsFilePath);
                Dictionary<string, string> secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                Console.WriteLine($"SecretsManager.LoadSecrets: Successfully loaded {secrets.Count} secrets");
                return secrets;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SecretsManager.LoadSecrets: Error loading secrets - {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private void SaveSecrets()
        {
            try
            {
                string json = JsonSerializer.Serialize(_secrets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_secretsFilePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save secrets: {ex.Message}", ex);
            }
        }

        public string GetSecretsFilePath() => _secretsFilePath;
    }
}