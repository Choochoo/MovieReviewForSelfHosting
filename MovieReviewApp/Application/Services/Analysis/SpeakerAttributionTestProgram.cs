using Microsoft.Extensions.Logging;
using MovieReviewApp.Application.Services.Analysis;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Test program to run speaker attribution fix on existing transcription files.
/// </summary>
public class SpeakerAttributionTestProgram
{
    public static async Task RunSpeakerAttributionFix(string sessionPath, ILogger logger)
    {
        // Create the service
        ILogger<SpeakerAttributionFixService> serviceLogger = logger as ILogger<SpeakerAttributionFixService> 
            ?? new LoggerFactory().CreateLogger<SpeakerAttributionFixService>();
            
        SpeakerAttributionFixService service = new(serviceLogger);

        Console.WriteLine("=== Speaker Attribution Fix Tool ===");
        Console.WriteLine($"Session Path: {sessionPath}");
        Console.WriteLine();

        // Step 1: Analyze existing files
        Console.WriteLine("Step 1: Analyzing transcription files...");
        TranscriptionAnalysisReport analysisReport = await service.AnalyzeTranscriptionFiles(sessionPath);
        
        Console.WriteLine($"Master Mix Found: {analysisReport.MasterMixFound}");
        if (analysisReport.MasterMixFound)
        {
            Console.WriteLine($"  - Utterances: {analysisReport.MasterMixUtteranceCount}");
        }
        
        Console.WriteLine($"Mic Files Found: {analysisReport.TotalMicFilesFound}");
        foreach (var kvp in analysisReport.MicFilesFound)
        {
            Console.WriteLine($"  - MIC{kvp.Key}: {analysisReport.MicFileUtteranceCounts.GetValueOrDefault(kvp.Key, 0)} utterances");
            if (analysisReport.MicFileSpeakerAlwaysZero.GetValueOrDefault(kvp.Key, false))
            {
                Console.WriteLine($"    WARNING: All utterances have speaker=0");
            }
        }

        if (analysisReport.Errors.Any())
        {
            Console.WriteLine("\nErrors during analysis:");
            foreach (string error in analysisReport.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        Console.WriteLine();

        // Step 2: Fix speaker attribution
        Console.WriteLine("Step 2: Fixing speaker attribution...");
        // Create mock mic assignments for testing
        Dictionary<int, string> micAssignments = new Dictionary<int, string>
        {
            { 1, "Jared" },
            { 2, "Lacey" }
        };
        SpeakerAttributionResult result = await service.FixSpeakerAttribution(sessionPath, micAssignments);

        if (result.Success)
        {
            Console.WriteLine($"SUCCESS! Fixed speaker attribution saved to: {result.OutputFilePath}");
            Console.WriteLine();
            Console.WriteLine("=== Statistics ===");
            Console.WriteLine($"Total Utterances: {result.TotalUtterances}");
            Console.WriteLine($"Matched: {result.MatchedUtterances} ({(double)result.MatchedUtterances / result.TotalUtterances * 100:F1}%)");
            Console.WriteLine($"Unmatched: {result.UnmatchedUtterances} ({(double)result.UnmatchedUtterances / result.TotalUtterances * 100:F1}%)");
            Console.WriteLine();
            
            Console.WriteLine("Utterances per person:");
            foreach (var kvp in result.UtterancesPerPerson.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  - {kvp.Key}: {kvp.Value} utterances");
            }

            if (result.UnmatchedTexts.Any())
            {
                Console.WriteLine();
                Console.WriteLine($"Sample of unmatched utterances (showing first 5 of {result.UnmatchedTexts.Count}):");
                foreach (string text in result.UnmatchedTexts.Take(5))
                {
                    Console.WriteLine($"  - {text}");
                }
            }
        }
        else
        {
            Console.WriteLine($"FAILED: {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// Example of how to call this from a command line or integration point.
    /// This Main method is commented out to avoid conflicts with the web application's entry point.
    /// To use this as a standalone console app, uncomment this method and create a separate console project.
    /// </summary>
    /*
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: SpeakerAttributionFix <session-path>");
            Console.WriteLine("Example: SpeakerAttributionFix /path/to/uploads/sessions/2024-11-01_Solaris");
            return 1;
        }

        string sessionPath = args[0];
        if (!Directory.Exists(sessionPath))
        {
            Console.WriteLine($"Error: Directory not found: {sessionPath}");
            return 1;
        }

        // Create a simple console logger
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        
        ILogger logger = loggerFactory.CreateLogger<SpeakerAttributionTestProgram>();

        try
        {
            await RunSpeakerAttributionFix(sessionPath, logger);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
            logger.LogError(ex, "Failed to run speaker attribution fix");
            return 1;
        }
    }
    */
}