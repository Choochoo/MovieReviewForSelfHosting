#!/bin/bash

# Run the speaker attribution fix test on the existing Solaris session
SESSION_PATH="/mnt/c/Users/Jared/source/repos/Choochoo/MovieReviewApp/MovieReviewApp/wwwroot/uploads/sessions/2024-11-01_Solaris"

echo "Running Speaker Attribution Fix Test"
echo "Session Path: $SESSION_PATH"
echo "=================================="

# Navigate to the project directory
cd /mnt/c/Users/Jared/source/repos/Choochoo/MovieReviewApp/MovieReviewApp

# Create a simple test program to run the service
cat > TestSpeakerFix.cs << 'EOF'
using Microsoft.Extensions.Logging;
using MovieReviewApp.Application.Services.Analysis;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<SpeakerAttributionTestProgram>();
var sessionPath = args.Length > 0 ? args[0] : "/mnt/c/Users/Jared/source/repos/Choochoo/MovieReviewApp/MovieReviewApp/wwwroot/uploads/sessions/2024-11-01_Solaris";

await SpeakerAttributionTestProgram.RunSpeakerAttributionFix(sessionPath, logger);
EOF

# Run the test
dotnet run --project . TestSpeakerFix.cs -- "$SESSION_PATH"

# Clean up
rm -f TestSpeakerFix.cs

echo "=================================="
echo "Test completed. Check the output above for results."
echo "The fixed file should be at: $SESSION_PATH/master_mix_with_speakers.json"