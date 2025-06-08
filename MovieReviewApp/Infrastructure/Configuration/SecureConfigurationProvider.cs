using Microsoft.Extensions.Primitives;

namespace MovieReviewApp.Infrastructure.Configuration
{
    public class SecureConfigurationProvider : IConfigurationProvider
    {
        private readonly SecretsManager _secretsManager;
        private readonly Dictionary<string, string> _data;

        public SecureConfigurationProvider(SecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
            _data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Load()
        {
            _data.Clear();

            // Load secrets from SecretsManager
            var secrets = new[]
            {
                "TMDB:ApiKey",
                "MongoDB:ConnectionString",
                "Gladia:ApiKey",
                "Facebook:ChatUrl",
                "App:DisplayName"
            };

            Console.WriteLine("SecureConfigurationProvider: Loading secrets...");
            foreach (var secret in secrets)
            {
                var value = _secretsManager.GetSecret(secret);
                if (!string.IsNullOrEmpty(value))
                {
                    _data[secret] = value;
                    Console.WriteLine($"SecureConfigurationProvider: Loaded {secret} (length: {value.Length})");
                }
                else
                {
                    Console.WriteLine($"SecureConfigurationProvider: {secret} is empty/null");
                }
            }
            Console.WriteLine($"SecureConfigurationProvider: Loaded {_data.Count} secrets total");
        }

        public bool TryGet(string key, out string value)
        {
            return _data.TryGetValue(key, out value);
        }

        public void Set(string key, string? value)
        {
            if (value != null)
            {
                _data[key] = value;
            }
            else
            {
                _data.Remove(key);
            }
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            var results = new List<string>();

            if (parentPath == null)
            {
                foreach (var kvp in _data)
                {
                    results.Add(Segment(kvp.Key, 0));
                }
            }
            else
            {
                foreach (var kvp in _data)
                {
                    if (kvp.Key.Length > parentPath.Length &&
                        kvp.Key.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase) &&
                        kvp.Key[parentPath.Length] == ':')
                    {
                        results.Add(Segment(kvp.Key, parentPath.Length + 1));
                    }
                }
            }

            results.AddRange(earlierKeys);
            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string Segment(string key, int prefixLength)
        {
            var indexOf = key.IndexOf(':', prefixLength);
            return indexOf < 0 ? key.Substring(prefixLength) : key.Substring(prefixLength, indexOf - prefixLength);
        }

        public IChangeToken GetReloadToken()
        {
            return new ConfigurationReloadToken();
        }
    }

    public class SecureConfigurationSource : IConfigurationSource
    {
        private readonly SecretsManager _secretsManager;

        public SecureConfigurationSource(SecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            var provider = new SecureConfigurationProvider(_secretsManager);
            provider.Load(); // Ensure secrets are loaded
            return provider;
        }
    }

    public class ConfigurationReloadToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new NoOpDisposable();

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}