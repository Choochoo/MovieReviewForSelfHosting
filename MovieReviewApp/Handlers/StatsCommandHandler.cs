using MovieReviewApp.Enums;
using MovieReviewApp.Helpers;
using MovieReviewApp.Application.Services;

namespace MovieReviewApp.Handlers
{
    public class StatsCommandHandler
    {
        private readonly MovieReviewService _movieReviewService;
        private readonly string _webRootPath;
        private readonly HashSet<string> _commonWords;

        public StatsCommandHandler(string webRootPath, MovieReviewService movieReviewService)
        {
            _webRootPath = webRootPath;
            _movieReviewService = movieReviewService;
            _commonWords = LoadCommonWords();
        }

        public async Task<List<string>> ExecuteCommand(string folderName, StatsCommandType commandType)
        {
            switch (commandType)
            {
                case StatsCommandType.ScanMostPopularWord:
                    return await ScanMostPopularWord(folderName);
                // Add cases for other commands
                default:
                    throw new NotImplementedException();
            }
        }

        private Task<List<string>> ScanMostPopularWord(string folderName)
        {
            // Read text files in the folder
            string[] allFilesText = FolderHelper.GetTextFromFiles(Path.Combine(_webRootPath, "uploads", folderName));
            string text = string.Join(Environment.NewLine, allFilesText);

            // Split text into words, remove common words, and count word frequency
            List<string> words = text
                .Split(new[] { ' ', '.', ',', ';', ':', '?', '!', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLower())
                .Where(w => !_commonWords.Contains(w))  // Exclude common words
                .GroupBy(w => w)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            return Task.FromResult(words);
        }

        private HashSet<string> LoadCommonWords()
        {
            string commonWordsFilePath = Path.Combine(_webRootPath, "assets", "common_words.txt");

            if (File.Exists(commonWordsFilePath))
            {
                try
                {
                    HashSet<string> commonWords = File.ReadAllLines(commonWordsFilePath)
                                          .Select(word => word.ToLower().Trim())
                                          .Where(word => !string.IsNullOrWhiteSpace(word))
                                          .ToHashSet();
                    return commonWords;
                }
                catch
                {
                    // Fall back to default common words if file can't be read
                }
            }

            // Default common words if file doesn't exist or can't be read
            return new HashSet<string>
            {
                "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "is", "are", "was", "were",
                "be", "been", "being", "have", "has", "had", "do", "does", "did", "will", "would", "could", "should",
                "may", "might", "must", "can", "cannot", "a", "an", "this", "that", "these", "those", "i", "you", "he",
                "she", "it", "we", "they", "me", "him", "her", "us", "them", "my", "your", "his", "her", "its", "our",
                "their", "mine", "yours", "hers", "ours", "theirs", "am", "so", "very", "just", "now", "then", "here",
                "there", "where", "when", "why", "how", "what", "who", "which", "whose", "whom", "all", "any", "some",
                "many", "much", "more", "most", "few", "little", "less", "least", "no", "not", "only", "also", "too",
                "as", "than", "like", "um", "uh", "yeah", "okay", "well", "right", "actually", "basically", "literally"
            };
        }
    }
}
