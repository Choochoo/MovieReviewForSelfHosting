using System.Text;
using System.Text.Json;

namespace MovieReviewApp.Infrastructure.Services
{
    public class MessengerService
    {
        private readonly HttpClient _httpClient;
        private readonly string _chatUrl;

        public MessengerService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _chatUrl = configuration["AppSettings:ChatUrl"];
        }

        public async Task SendMovieUpdateMessage(string personName, string movieName)
        {
            if (string.IsNullOrEmpty(_chatUrl))
                return;

            string message = $"---Automated Message--- \n{personName} just updated the site, adding the movie {movieName}\n------------End-------------";

            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { chatUrl = _chatUrl, message = message }),
                Encoding.UTF8,
                "application/json"
            );

            try
            {
                await _httpClient.PostAsync("http://localhost:5014/api/messenger/queueMessage", content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send messenger update: {ex.Message}");
                // Don't throw - we don't want to break the save operation if messaging fails
            }
        }
    }
}
