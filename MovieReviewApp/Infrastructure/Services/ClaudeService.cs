using System.Text;
using System.Text.Json;
using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Infrastructure.Services;

public class ClaudeService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClaudeService> _logger;
    private readonly SecretsManager _secretsManager;
    private readonly string _apiKey;
    private readonly string _baseUrl = "https://api.anthropic.com";

    public ClaudeService(HttpClient httpClient, IConfiguration configuration, SecretsManager secretsManager, ILogger<ClaudeService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _secretsManager = secretsManager;
        _logger = logger;

        // Get API key from secrets manager directly
        _apiKey = _secretsManager.GetSecret("Claude:ApiKey") ?? string.Empty;

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            _logger.LogInformation("Claude service initialized with API key");
        }
        else
        {
            _logger.LogWarning("Claude service initialized without API key - text spicing will be disabled");
        }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<string?> ExecutePromptAsync(string prompt)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Claude API key not configured - cannot execute prompt");
            return null;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        try
        {
            object requestBody = new
            {
                model = "claude-sonnet-4-20250514",
                max_tokens = 1000,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            string json = JsonSerializer.Serialize(requestBody);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"{_baseUrl}/v1/messages", content);

            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (responseJson.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                {
                    JsonElement firstContent = contentArray[0];
                    if (firstContent.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString();
                    }
                }
            }
            else
            {
                _logger.LogError("Claude API request failed with status: {StatusCode}", response.StatusCode);
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Claude API error response: {ErrorContent}", errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Claude API to execute prompt");
        }

        return null;
    }
}
