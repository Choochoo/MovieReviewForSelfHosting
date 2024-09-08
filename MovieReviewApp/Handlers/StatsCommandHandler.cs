using MovieReviewApp.Database;
using MovieReviewApp.Enums;
using MovieReviewApp.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace MovieReviewApp.Handlers
{
    public class StatsCommandHandler
    {
        private MongoDb _db = new MongoDb();
        private readonly string _webRootPath;
        private HashSet<string> CommonWords;

        public StatsCommandHandler(string webRootPath)
        {
            _webRootPath = webRootPath;
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
            var text = string.Join(Environment.NewLine, allFilesText);

            // Split text into words, remove common words, and count word frequency
            var words = text
                .Split(new[] { ' ', '.', ',', ';', ':', '?', '!', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLower())
                .Where(w => !CommonWords.Contains(w))  // Exclude common words
                .GroupBy(w => w)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            return Task.FromResult(words);
        }

        private HashSet<string> LoadCommonWords()
        {
            var commonWordsFilePath = Path.Combine(_webRootPath, "assets", "common_words.txt"); // Adjust the path as needed

            if (!File.Exists(commonWordsFilePath))
            {
                throw new FileNotFoundException("Common words file not found", commonWordsFilePath);
            }

            var commonWords = File.ReadAllLines(commonWordsFilePath)
                                  .Select(word => word.ToLower().Trim())
                                  .Where(word => !string.IsNullOrWhiteSpace(word))
                                  .ToHashSet();

            return commonWords;
        }
    }
}
