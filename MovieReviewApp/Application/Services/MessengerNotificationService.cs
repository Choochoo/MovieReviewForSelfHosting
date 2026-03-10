using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

public class MessengerNotificationService
{
    private const string SiteUrl = "http://ourfilmclub.duckdns.org:5010/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FacebookSettings _settings;
    private readonly ILogger<MessengerNotificationService> _logger;

    public MessengerNotificationService(
        IHttpClientFactory httpClientFactory,
        IOptions<FacebookSettings> settings,
        ILogger<MessengerNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_settings.ChatUrl) &&
        !string.IsNullOrEmpty(_settings.ApiBaseUrl) &&
        !string.IsNullOrEmpty(_settings.ApiKey);

    public async Task NotifyAsync(string message)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Messenger notification skipped - not configured");
            return;
        }

        try
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);

            object payload = new { Url = _settings.ChatUrl, Message = message };
            StringContent content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            string url = _settings.ApiBaseUrl!.TrimEnd('/') + "/api/notify";
            HttpResponseMessage response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Messenger notification failed ({StatusCode}): {Body}",
                    response.StatusCode, body);
            }
            else
            {
                _logger.LogInformation("Messenger notification sent: {Message}", message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send messenger notification");
        }
    }

    public Task NotifyMovieAddedAsync(string? person)
    {
        string who = person ?? "Someone";
        return NotifyAsync($"Movie Club Update - {who} just added their movie for the month, go check it out! {SiteUrl}");
    }

    public Task NotifyCategoryVotingDoneAsync(string? voterName, bool wasLastVoter)
    {
        string who = voterName ?? "Someone";
        string msg = wasLastVoter
            ? $"Movie Club Update - {who} is done voting for Categories! They were the last one, the results are in!"
            : $"Movie Club Update - {who} is done voting for Categories!";
        return NotifyAsync(msg);
    }

    public Task NotifyAwardVotingDoneAsync(string? voterName, bool wasLastVoter)
    {
        string who = voterName ?? "Someone";
        string msg = wasLastVoter
            ? $"Movie Club Update - {who} is done voting for award voting! They were the last one, the results are in!"
            : $"Movie Club Update - {who} is done voting for award voting!";
        return NotifyAsync(msg);
    }

    public Task NotifySiteUpdateAsync(string description)
    {
        return NotifyAsync($"Movie Club Update - {description}");
    }
}
