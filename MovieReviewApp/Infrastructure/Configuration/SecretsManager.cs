using System.Text.Json;

namespace MovieReviewApp.Infrastructure.Configuration
{
    public class SecretsManager
    {
        private readonly string _secretsFilePath;
        private readonly InstanceManager _instanceManager;
        private Dictionary<string, string> _secrets;

        public SecretsManager(InstanceManager instanceManager)
        {
            _instanceManager = instanceManager;
            _secretsFilePath = instanceManager.SecretsPath;
            Console.WriteLine($"SecretsManager: Loading secrets from {_secretsFilePath}");
            _secrets = LoadSecrets();
            Console.WriteLine($"SecretsManager: Loaded {_secrets.Count} secrets");
        }

        public bool IsFirstRun => _instanceManager.IsFirstRun;

        public string? GetSecret(string key)
        {
            return _secrets.TryGetValue(key, out var value) ? value : null;
        }

        public void SetSecret(string key, string value)
        {
            _secrets[key] = value;
            SaveSecrets();
        }

        public void SetSecrets(Dictionary<string, string> secrets)
        {
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