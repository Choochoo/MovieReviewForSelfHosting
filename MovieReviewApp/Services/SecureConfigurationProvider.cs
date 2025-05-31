using Microsoft.Extensions.Primitives;

namespace MovieReviewApp.Services
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

            foreach (var secret in secrets)
            {
                var value = _secretsManager.GetSecret(secret);
                if (!string.IsNullOrEmpty(value))
                {
                    _data[secret] = value;
                }
            }
        }

        public bool TryGet(string key, out string value)
        {
            return _data.TryGetValue(key, out value);
        }

        public void Set(string key, string value)
        {
            _data[key] = value;
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string parentPath)
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
            return new SecureConfigurationProvider(_secretsManager);
        }
    }

    public class ConfigurationReloadToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object> callback, object state) => new NoOpDisposable();

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}