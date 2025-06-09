using MovieReviewApp.Application.Models.Transcription;
using MovieReviewApp.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service responsible for processing and formatting transcripts from movie sessions.
/// </summary>
public class TranscriptProcessingService
{
    private readonly ILogger<TranscriptProcessingService> _logger;

    public TranscriptProcessingService(ILogger<TranscriptProcessingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds a combined transcript from all audio files in a movie session.
    /// </summary>
    /// <param name="session">The movie session containing audio files with transcripts.</param>
    /// <returns>A formatted transcript combining all audio sources.</returns>
    public string BuildCombinedTranscript(MovieSession session)
    {
        StringBuilder transcriptBuilder = new StringBuilder();

        // Add session context
        transcriptBuilder.AppendLine("=== TRANSCRIPT ANALYSIS CONTEXT ===");
        transcriptBuilder.AppendLine($"Movie: {session.MovieTitle}");
        transcriptBuilder.AppendLine($"Date: {session.Date:yyyy-MM-dd}");
        transcriptBuilder.AppendLine($"Participants: {string.Join(", ", session.ParticipantsPresent)}");
        transcriptBuilder.AppendLine();

        // Get master recording and individual files
        AudioFile? masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording && !string.IsNullOrEmpty(f.TranscriptText));
        List<AudioFile> individualFiles = session.AudioFiles.Where(f => !f.IsMasterRecording && !string.IsNullOrEmpty(f.TranscriptText)).ToList();

        _logger.LogInformation("Building transcript from {TotalFiles} audio files: Master={MasterFile}, Individual={IndividualCount}",
            session.AudioFiles.Count,
            masterFile?.FileName ?? "None",
            individualFiles.Count);

        foreach (AudioFile file in session.AudioFiles)
        {
            _logger.LogDebug("Audio file {FileName}: IsMaster={IsMaster}, HasTranscript={HasTranscript}, TranscriptLength={Length}",
                file.FileName,
                file.IsMasterRecording,
                !string.IsNullOrEmpty(file.TranscriptText),
                file.TranscriptText?.Length ?? 0);
        }

        // Strategy: Prioritize master recording to avoid duplication
        if (masterFile != null)
        {
            _logger.LogInformation("Using master recording for analysis (size: {Size} chars)", masterFile.TranscriptText.Length);

            transcriptBuilder.AppendLine("=== MASTER RECORDING (Full Group Conversation) ===");
            transcriptBuilder.AppendLine("⚠️  IMPORTANT: Use ONLY timestamps from this master recording for audio clips!");
            transcriptBuilder.AppendLine("This captures everyone talking together with natural overlaps and interruptions.");
            transcriptBuilder.AppendLine("All audio clips will be generated from this file, so timestamps must match this timeline.");
            transcriptBuilder.AppendLine();

            string masterTranscript = masterFile.TranscriptText;

            // Convert JSON format to plain text
            masterTranscript = ConvertJsonTranscriptToPlainText(masterTranscript, "Speaker", session.ParticipantsPresent);

            transcriptBuilder.Append(masterTranscript);
        }
        else if (individualFiles.Any())
        {
            _logger.LogInformation("No master recording found, using {Count} individual recordings", individualFiles.Count);
            transcriptBuilder.AppendLine("=== INDIVIDUAL RECORDINGS (Merged) ===");
            transcriptBuilder.AppendLine("Note: These are separate mic recordings merged together.");
            transcriptBuilder.AppendLine();

            foreach (AudioFile file in individualFiles)
            {
                string speakerName = GetSpeakerName(file, session);
                transcriptBuilder.AppendLine($"\n--- {speakerName} (from {file.FileName}) ---");

                string transcript = file.TranscriptText;
                transcript = ConvertJsonTranscriptToPlainText(transcript, speakerName, session.ParticipantsPresent);

                transcriptBuilder.Append(transcript);
            }
        }

        return transcriptBuilder.ToString();
    }

    /// <summary>
    /// Converts JSON transcript format to plain text with speaker labels.
    /// </summary>
    public string ConvertJsonTranscriptToPlainText(string jsonTranscript, string speakerName, List<string> participantsPresent)
    {
        try
        {
            // Check if it's actually JSON
            if (!jsonTranscript.TrimStart().StartsWith("{") && !jsonTranscript.TrimStart().StartsWith("["))
            {
                // Not JSON, return as-is
                return jsonTranscript;
            }

            TranscriptData? transcriptData = JsonSerializer.Deserialize<TranscriptData>(jsonTranscript);
            StringBuilder sb = new StringBuilder();

            if (transcriptData?.utterances != null)
            {
                foreach (WordCountUtterance utterance in transcriptData.utterances)
                {
                    // Skip empty utterances
                    if (string.IsNullOrWhiteSpace(utterance.text))
                        continue;

                    // For individual mic files, use the provided speaker name
                    // For master mix, if there's only one participant, use their name
                    // Otherwise use a generic speaker label
                    string actualSpeaker = speakerName;

                    if (speakerName == "Speaker" && participantsPresent.Count == 1)
                    {
                        actualSpeaker = participantsPresent[0];
                    }

                    sb.AppendLine($"{actualSpeaker}: {utterance.text.Trim()}");
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON transcript, returning as-is");
            return jsonTranscript;
        }
    }

    /// <summary>
    /// Truncates transcript to maximum size with warning message.
    /// </summary>
    public string TruncateTranscript(string transcript, int maxSize, string warningTemplate)
    {
        if (transcript.Length <= maxSize)
            return transcript;

        string truncated = transcript.Substring(0, maxSize);
        string warning = string.Format(warningTemplate, maxSize);
        return truncated + warning;
    }

    /// <summary>
    /// Gets the speaker name for an audio file based on session mic assignments.
    /// </summary>
    private string GetSpeakerName(AudioFile file, MovieSession session)
    {
        if (file.SpeakerNumber.HasValue && session.MicAssignments.TryGetValue(file.SpeakerNumber.Value, out string? assignedName))
        {
            return assignedName;
        }

        // Try to extract from filename
        Match speakerMatch = Regex.Match(file.FileName, @"Speaker\s*(\d+)", RegexOptions.IgnoreCase);
        if (speakerMatch.Success)
        {
            return $"Speaker {speakerMatch.Groups[1].Value}";
        }

        return "Unknown Speaker";
    }
}