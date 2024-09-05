using MovieReviewApp.Database;
using MovieReviewApp.Enums;

namespace MovieReviewApp.Handlers
{
    public class StatsCommandHandler
    {
        private MongoDb _db = new MongoDb();
        private static readonly HashSet<string> CommonWords = new HashSet<string>
    {
        "the", "is", "in", "it", "and", "or", "to", "a", "of", "on", "at", "as", "for", "with", "by",
        "an", "be", "this", "that", "but", "not", "are", "from", "was", "were", "you", "he", "she", "we",
        "they", "has", "have", "had", "will", "can", "do", "if", "when", "which", "who", "their", "its"
        // Add more common words to this list as needed
    };

        public StatsCommandHandler()
        {
        }

        public async Task<List<string>> ExecuteCommand(StatsCommandType commandType, string text)
        {
            switch (commandType)
            {
                case StatsCommandType.ScanMostPopularWord:
                    return await ScanMostPopularWord(text);
                // Add cases for other commands
                default:
                    throw new NotImplementedException();
            }
        }

        private Task<List<string>> ScanMostPopularWord(string text)
        {
            // Split text into words, remove common words, and count word frequency
            var words = text
                .Split(new[] { ' ', '.', ',', ';', ':', '?', '!', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLower())
                .Where(w => !CommonWords.Contains(w))
                .GroupBy(w => w)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            return Task.FromResult(words);
        }
    }

}
