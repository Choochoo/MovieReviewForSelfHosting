using System.Text.Json;

namespace MovieReviewApp.Services
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
            _secrets = LoadSecrets();
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
            var requiredKeys = new[]
            {
                "TMDB:ApiKey",
                "MongoDB:ConnectionString"
            };

            return requiredKeys.All(key => !string.IsNullOrEmpty(GetSecret(key)));
        }

        public List<string> GetMissingSecrets()
        {
            var requiredKeys = new[]
            {
                "TMDB:ApiKey",
                "MongoDB:ConnectionString"
            };

            return requiredKeys.Where(key => string.IsNullOrEmpty(GetSecret(key))).ToList();
        }

        private Dictionary<string, string> LoadSecrets()
        {
            if (!File.Exists(_secretsFilePath))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                var json = File.ReadAllText(_secretsFilePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        private void SaveSecrets()
        {
            try
            {
                var json = JsonSerializer.Serialize(_secrets, new JsonSerializerOptions { WriteIndented = true });
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