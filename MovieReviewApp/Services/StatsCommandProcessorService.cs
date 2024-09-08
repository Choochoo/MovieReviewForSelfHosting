using MovieReviewApp.Database;
using MovieReviewApp.Enums;
using MovieReviewApp.Handlers;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class StatsCommandProcessorService
    {
        private MongoDb _db = new MongoDb();
        private readonly StatsCommandHandler statsCommandHandler;

        public StatsCommandProcessorService(string webRootPath)
        {
            statsCommandHandler = new StatsCommandHandler(webRootPath);
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
                    var result = await statsCommandHandler.ExecuteCommand(folderName, commandType.Value);
                    var commandResult = new StatsCommand
                    {
                        Command = command,
                        Results = result,
                        ProcessedDate = DateTime.Now,
                        FolderName = folderName
                    };
                    _db.AddStatsCommand(commandResult);
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
                ProcessedDate = DateTime.Now,
                FolderName = folder
            };

            // Save the result (this could be a database operation)
            _db.AddStatsCommand(statsCommand);

            await Task.CompletedTask;
        }
    }
}
