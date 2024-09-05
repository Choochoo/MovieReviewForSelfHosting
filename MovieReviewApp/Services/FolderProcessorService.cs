using MovieReviewApp.Enums;
using MovieReviewApp.Handlers;

namespace MovieReviewApp.Services
{
    public class FolderProcessorService
    {
        private readonly StatsCommandProcessorService _processorService;
        private readonly StatsCommandHandler _commandHandler;

        public FolderProcessorService(StatsCommandProcessorService processorService, StatsCommandHandler commandHandler)
        {
            _processorService = processorService;
            _commandHandler = commandHandler;
        }

        public async Task ProcessAllFolders(List<string> folderNames, List<StatsCommandType> commands)
        {
            foreach (var folder in folderNames)
            {
                // Get the text to process from the folder
                var textToProcess = await GetTextFromFolder(folder);

                foreach (var command in commands)
                {
                    // Execute the command and get the results (a list of strings)
                    var results = await _commandHandler.ExecuteCommand(command, textToProcess);

                    // Now, instead of results.Select(r => r.Command), we will save the results directly
                    // Process the results (this could be saving to the database, logging, or displaying)
                    await _processorService.ProcessCommandResults(folder, command, results);
                }
            }
        }


        private Task<string> GetTextFromFolder(string folderName)
        {
            // Logic to get the text from a folder, replace this with actual file reading/database code
            return Task.FromResult($"Text data from {folderName}");
        }
    }

}
