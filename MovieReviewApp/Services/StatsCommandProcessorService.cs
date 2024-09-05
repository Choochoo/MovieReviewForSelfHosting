using MovieReviewApp.Database;
using MovieReviewApp.Enums;
using MovieReviewApp.Models;

namespace MovieReviewApp.Services
{
    public class StatsCommandProcessorService
    {
        private MongoDb _db = new MongoDb();

        public StatsCommandProcessorService()
        {
        }

        public async Task<List<StatsCommand>> ProcessCommands(string folderName, List<string> commands)
        {
            var results = new List<StatsCommand>();
            var existingCommands = _db.GetProcessedStatCommandsForMonth(folderName);

            foreach (var command in commands)
            {
                if (!existingCommands.Any(c => c.Command == command))
                {
                    var result = ExecuteCommand(command); // This would be your actual command logic
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
            }

            return results;
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



        private List<string> ExecuteCommand(string command)
        {
            // Placeholder for actual command execution logic
            return new List<string> { $"Result for {command}" };
        }
    }
}
