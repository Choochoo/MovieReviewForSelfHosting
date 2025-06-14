using System.Text;
using System.Text.Json;
using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Infrastructure.Services;

public class OpenAIService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIService> _logger;
    private readonly SecretsManager _secretsManager;
    private readonly string _apiKey;
    private readonly string _baseUrl = "https://api.openai.com";

    public OpenAIService(HttpClient httpClient, IConfiguration configuration, SecretsManager secretsManager, ILogger<OpenAIService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _secretsManager = secretsManager;
        _logger = logger;

        // Get API key from secrets manager directly
        _apiKey = _secretsManager.GetSecret("OpenAI:ApiKey") ?? string.Empty;

        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _logger.LogInformation("OpenAI service initialized with API key");
        }
        else
        {
            _logger.LogWarning("OpenAI service initialized without API key - text spicing will be disabled");
        }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<string?> ExecutePromptAsync(string prompt)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("OpenAI API key not configured - cannot execute prompt");
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
                model = "GPT-4o",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                max_tokens = 1000,
                temperature = 0.7
            };

            string json = JsonSerializer.Serialize(requestBody);
            StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content);

            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                JsonElement responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (responseJson.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    JsonElement firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var contentElement))
                    {
                        return contentElement.GetString()?.Trim();
                    }
                }
            }
            else
            {
                _logger.LogError("OpenAI API request failed with status: {StatusCode}", response.StatusCode);
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error response: {ErrorContent}", errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API to execute prompt");
        }

        return null;
    }
}
