using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Models;
using MovieReviewApp.Application.Models.OpenAI;
using System.Text;
using System.Text.Json;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for communicating with the OpenAI API.
/// Handles API authentication, request/response management, and retry logic.
/// </summary>
public class OpenAIApiService
{
    private readonly HttpClient _httpClient;
    private readonly SecretsManager _secretsManager;
    private readonly ILogger<OpenAIApiService> _logger;
    private readonly string _openAiApiKey;
    private readonly string _baseUrl = "https://api.openai.com";

    public OpenAIApiService(
        HttpClient httpClient,
        SecretsManager secretsManager,
        ILogger<OpenAIApiService> logger)
    {
        _httpClient = httpClient;
        _secretsManager = secretsManager;
        _logger = logger;
        _openAiApiKey = _secretsManager.GetSecret("OpenAI:ApiKey") ?? string.Empty;

        if (!string.IsNullOrEmpty(_openAiApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAiApiKey}");
            _logger.LogInformation("OpenAI API service initialized with API key");
        }
        else
        {
            _logger.LogWarning("OpenAI API service initialized without API key - analysis will be disabled");
        }
    }

    /// <summary>
    /// Gets a value indicating whether the service is properly configured with an OpenAI API key.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrEmpty(_openAiApiKey);

    /// <summary>
    /// Analyzes transcript using OpenAI API.
    /// </summary>
    public async Task<string> AnalyzeTranscriptAsync(string prompt)
    {
        string? result = await ExecutePromptAsync(prompt, 4000, 0.3);
        return result ?? string.Empty;
    }

    /// <summary>
    /// Executes a prompt with the OpenAI API with automatic retry logic.
    /// </summary>
    public async Task<string?> ExecutePromptAsync(string prompt, int maxTokens = 1000, double temperature = 0.7)
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

        int maxRetries = 3;
        int currentRetry = 0;
        TimeSpan baseDelay = TimeSpan.FromSeconds(10);

        while (currentRetry < maxRetries)
        {
            try
            {
                _logger.LogInformation("Attempting OpenAI API call (attempt {Attempt}/{MaxAttempts})", currentRetry + 1, maxRetries);
                
                return await CallOpenAIForAnalysis(prompt, maxTokens, temperature);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("rate limit") || ex.Message.Contains("429"))
            {
                currentRetry++;
                if (currentRetry >= maxRetries)
                {
                    _logger.LogError("OpenAI rate limit exceeded after {MaxRetries} attempts", maxRetries);
                    throw;
                }

                TimeSpan delay = TimeSpan.FromSeconds(baseDelay.TotalSeconds * Math.Pow(2, currentRetry - 1));
                _logger.LogWarning("Rate limit hit, waiting {Delay} seconds before retry {Retry}/{MaxRetries}", 
                    delay.TotalSeconds, currentRetry + 1, maxRetries);
                
                await Task.Delay(delay);
            }
            catch (TaskTimeoutException)
            {
                currentRetry++;
                if (currentRetry >= maxRetries)
                {
                    _logger.LogError("OpenAI request timed out after {MaxRetries} attempts", maxRetries);
                    throw;
                }

                TimeSpan delay = TimeSpan.FromSeconds(baseDelay.TotalSeconds * currentRetry);
                _logger.LogWarning("Request timeout, waiting {Delay} seconds before retry {Retry}/{MaxRetries}", 
                    delay.TotalSeconds, currentRetry + 1, maxRetries);
                
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI API call failed on attempt {Attempt}", currentRetry + 1);
                
                currentRetry++;
                if (currentRetry >= maxRetries)
                {
                    throw;
                }

                TimeSpan delay = TimeSpan.FromSeconds(baseDelay.TotalSeconds * currentRetry);
                await Task.Delay(delay);
            }
        }

        return null;
    }

    /// <summary>
    /// Makes a single call to the OpenAI API with the provided prompt.
    /// </summary>
    private async Task<string?> CallOpenAIForAnalysis(string prompt, int maxTokens, double temperature)
    {
        string requestBody = JsonSerializer.Serialize(new
        {
            model = "gpt-4",
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = maxTokens,
            temperature = temperature
        });

        using StringContent content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending request to OpenAI API with {TokenCount} estimated tokens", EstimateTokenCount(prompt));

        using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        
        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                OpenAIResponse? openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent);

                if (openAIResponse?.choices?.Length > 0 && openAIResponse.choices[0].message?.content != null)
                {
                    string result = openAIResponse.choices[0].message.content;
                    _logger.LogInformation("OpenAI analysis completed successfully: {ResultLength} characters", result.Length);
                    return result;
                }
                else
                {
                    throw new InvalidOperationException("OpenAI response was empty or invalid");
                }
            }
            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                string errorMessage = $"OpenAI API call failed with status {response.StatusCode}: {errorContent}";
                _logger.LogError(errorMessage);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException("rate limit exceeded");
                }

                throw new HttpRequestException(errorMessage);
            }
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            throw new TaskTimeoutException("OpenAI request timed out after 5 minutes");
        }
    }

    /// <summary>
    /// Estimates the token count for a given text (rough approximation).
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        return (int)(text.Length / 4.0); // Rough estimate: 1 token ≈ 4 characters
    }
}

/// <summary>
/// Exception thrown when an OpenAI request times out.
/// </summary>
public class TaskTimeoutException : Exception
{
    public TaskTimeoutException(string message) : base(message) { }
    public TaskTimeoutException(string message, Exception innerException) : base(message, innerException) { }
}