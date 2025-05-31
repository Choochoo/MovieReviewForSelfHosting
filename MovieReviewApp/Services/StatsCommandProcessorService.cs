using Microsoft.AspNetCore.Components;
using MovieReviewApp.Database;
using MovieReviewApp.Enums;
using MovieReviewApp.Handlers;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class StatsCommandProcessorService
    {
        private readonly MovieReviewService _movieReviewService;
        private readonly StatsCommandHandler _statsCommandHandler;

        public StatsCommandProcessorService(string webRootPath, MovieReviewService movieReviewService)
        {
            _movieReviewService = movieReviewService;
            _statsCommandHandler = new StatsCommandHandler(webRootPath, movieReviewService);
        }

        public async Task<List<StatsCommand>> ProcessCommands(string folderName, List<string> commands)
        {
            var results = new List<StatsCommand>();

            foreach (var command in commands)
            {
                // Try to find the corresponding StatsCommandType enum value
                StatsCommandType? commandType = GetCommandType(command);

                if (commandType.HasValue)
                {
                    // Execute the command using the command type
                    var result = await _statsCommandHandler.ExecuteCommand(folderName, commandType.Value);
                    var commandResult = new StatsCommand
                    {
                        Command = command,
                        Results = result,
                        ProcessedDate = DateProvider.Now,
                        FolderName = folderName
                    };
                    _movieReviewService.AddStatsCommand(commandResult);
                    results.Add(commandResult);
                }
                else
                {
                    // Log an error or handle the case where the command doesn't match an enum value
                    Console.WriteLine($"No matching command type found for {command}");
                }
            }

            return results;
        }

        // Helper method to map the string command to StatsCommandType
        private StatsCommandType? GetCommandType(string command)
        {
            // Loop through the StatsCommandType enum values
            foreach (var enumValue in Enum.GetValues(typeof(StatsCommandType)))
            {
                var enumName = enumValue.ToString().ToLower().Replace(" ", "");

                // Match the processed enum name with the command
                if (enumName == command.ToLower().Replace(" ", ""))
                {
                    return (StatsCommandType)enumValue;
                }
            }

            // Return null if no match is found
            return null;
        }

        public async Task ProcessCommandResults(string folder, StatsCommandType commandType, List<string> results)
        {
            // Convert the commandType to a string for saving in the model
            var commandString = commandType.ToString();

            // Create a new StatsCommand and set its properties
            var statsCommand = new StatsCommand
            {
                Command = commandString,
                Results = results, // Saving the entire list of results
                ProcessedDate = DateProvider.Now,
                FolderName = folder
            };

            // Save the result (this could be a database operation)
            _movieReviewService.AddStatsCommand(statsCommand);

            await Task.CompletedTask;
        }
    }
}
