using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MovieReviewApp.Application.Models.Transcription;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services.Analysis;

/// <summary>
/// Service to fix speaker attribution in transcription files by matching utterances
/// between master mix and individual mic files.
/// </summary>
public class SpeakerAttributionFixService
{
    private readonly ILogger<SpeakerAttributionFixService> _logger;
    private readonly WordAnalysisService _wordAnalysisService;

    public SpeakerAttributionFixService(ILogger<SpeakerAttributionFixService> logger, WordAnalysisService wordAnalysisService)
    {
        _logger = logger;
        _wordAnalysisService = wordAnalysisService;
    }

    /// <summary>
    /// Analyzes transcription files and reports on their structure and quality.
    /// Includes conversation statistics for debugging speaker attribution.
    /// </summary>
    public async Task<TranscriptionAnalysisReport> AnalyzeTranscriptionFiles(string sessionPath)
    {
        TranscriptionAnalysisReport report = new();

        try
        {
            // Find master mix file
            string masterMixPath = Path.Combine(sessionPath, "MASTER_MIX_transcription.json");
            if (File.Exists(masterMixPath))
            {
                report.MasterMixFound = true;
                GladiaTranscriptionResponse? masterData = await LoadTranscriptionFile(masterMixPath);
                if (masterData?.result?.transcription?.utterances != null)
                {
                    report.MasterMixUtteranceCount = masterData.result.transcription.utterances.Count;
                }
            }

            // Find individual mic files
            for (int micNumber = 1; micNumber <= 6; micNumber++)
            {
                string micPath = Path.Combine(sessionPath, $"MIC{micNumber}_transcription.json");
                if (File.Exists(micPath))
                {
                    report.MicFilesFound.Add(micNumber, micPath);
                    GladiaTranscriptionResponse? micData = await LoadTranscriptionFile(micPath);
                    if (micData?.result?.transcription?.utterances != null)
                    {
                        report.MicFileUtteranceCounts[micNumber] = micData.result.transcription.utterances.Count;

                        // Check if speaker is always 0
                        bool allSpeakerZero = micData.result.transcription.utterances.All(u => u.speaker == 0);
                        report.MicFileSpeakerAlwaysZero[micNumber] = allSpeakerZero;
                    }
                }
            }

            report.TotalMicFilesFound = report.MicFilesFound.Count;

            // Generate conversation statistics for debugging
            await PopulateConversationStatistics(report, sessionPath);

            _logger.LogInformation("Analysis complete: Master mix found={MasterFound}, Mic files found={MicCount}",
                report.MasterMixFound, report.TotalMicFilesFound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing transcription files");
            report.Errors.Add($"Analysis error: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Populates conversation statistics for debugging speaker attribution.
    /// Analyzes current state of transcripts to show word counts per speaker.
    /// </summary>
    private async Task PopulateConversationStatistics(TranscriptionAnalysisReport report, string sessionPath)
    {
        try
        {
            // Load master mix for analysis
            string masterMixPath = Path.Combine(sessionPath, "MASTER_MIX_transcription.json");
            if (!File.Exists(masterMixPath))
            {
                _logger.LogWarning("No master mix file found for conversation analysis");
                return;
            }

            GladiaTranscriptionResponse? masterData = await LoadTranscriptionFile(masterMixPath);
            if (masterData?.result?.transcription?.utterances == null)
            {
                _logger.LogWarning("Invalid master mix data for conversation analysis");
                return;
            }

            // Convert JSON transcript to plain text for analysis
            StringBuilder transcriptBuilder = new StringBuilder();
            foreach (WordCountUtterance utterance in masterData.result.transcription.utterances)
            {
                if (!string.IsNullOrWhiteSpace(utterance.text))
                {
                    // Use "Unknown" as speaker since we haven't fixed attribution yet
                    transcriptBuilder.AppendLine($"Unknown: {utterance.text.Trim()}");
                }
            }

            string combinedTranscript = transcriptBuilder.ToString();

            // Analyze transcript for basic stats
            AnalyzeTranscriptForConversationStats(combinedTranscript, report);

            _logger.LogDebug("Generated conversation stats: {TotalWords} words, {TotalUtterances} utterances",
                report.TotalWords, report.TotalUtterances);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate conversation statistics");
        }
    }

    /// <summary>
    /// Analyzes a transcript to extract conversation statistics for the report.
    /// Based on SimpleSessionStatsService.AnalyzeTranscriptForStats but adapted for TranscriptionAnalysisReport.
    /// </summary>
    private void AnalyzeTranscriptForConversationStats(string transcript, TranscriptionAnalysisReport report)
    {
        if (string.IsNullOrEmpty(transcript)) return;

        // Split transcript into speaker segments
        string[] lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            // Look for speaker pattern: "Speaker: text"
            Match speakerMatch = Regex.Match(line, @"^([^:]+):\s*(.+)$");
            if (!speakerMatch.Success) continue;

            string speaker = speakerMatch.Groups[1].Value.Trim();
            string text = speakerMatch.Groups[2].Value.Trim();

            if (string.IsNullOrEmpty(text)) continue;

            // Count words
            int wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            report.WordCountsPerSpeaker[speaker] = report.WordCountsPerSpeaker.GetValueOrDefault(speaker, 0) + wordCount;
            report.TotalWords += wordCount;

            // Count utterances
            report.UtteranceCountsPerSpeaker[speaker] = report.UtteranceCountsPerSpeaker.GetValueOrDefault(speaker, 0) + 1;
            report.TotalUtterances++;
        }

        // Determine conversation tone
        report.ConversationTone = DetermineConversationTone(transcript);
    }

    /// <summary>
    /// Determines conversation tone based on content analysis.
    /// </summary>
    private string DetermineConversationTone(string transcript)
    {
        if (string.IsNullOrEmpty(transcript)) return "Unknown";

        int laughterCount = Regex.Matches(transcript, @"\b(haha|lol|lmao|laughing|chuckle)\b", RegexOptions.IgnoreCase).Count;
        int interruptionCount = Regex.Matches(transcript, @"\b(wait|hold on|but|however)\b", RegexOptions.IgnoreCase).Count;
        int questionCount = transcript.Count(c => c == '?');

        if (laughterCount > 10 && interruptionCount < 5)
            return "Light-hearted and fun";
        else if (interruptionCount > 10)
            return "Heated and passionate";
        else if (questionCount > 15)
            return "Analytical and thoughtful";
        else if (laughterCount > 5)
            return "Engaging with good humor";
        else
            return "Calm and focused discussion";
    }

    /// <summary>
    /// Populates conversation statistics for the SpeakerAttributionResult using corrected speaker names.
    /// This analyzes the enhanced utterances AFTER speaker attribution fix.
    /// </summary>
    private async Task PopulateConversationStatisticsForResult(SpeakerAttributionResult result, List<EnhancedUtterance> enhancedUtterances)
    {
        try
        {
            // Convert enhanced utterances to plain text transcript for analysis
            StringBuilder transcriptBuilder = new StringBuilder();
            foreach (EnhancedUtterance utterance in enhancedUtterances)
            {
                if (!string.IsNullOrWhiteSpace(utterance.text))
                {
                    transcriptBuilder.AppendLine($"{utterance.speaker}: {utterance.text.Trim()}");
                }
            }

            string combinedTranscript = transcriptBuilder.ToString();

            // Analyze transcript for conversation stats
            AnalyzeTranscriptForResultStats(combinedTranscript, result);

            _logger.LogDebug("Generated conversation stats for result: {TotalWords} words, {TotalUtterances} utterances",
                result.TotalWords, result.TotalUtterances);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate conversation statistics for result");
        }
    }


    /// <summary>
    /// Analyzes transcript for conversation statistics in SpeakerAttributionResult.
    /// Enhanced with better pattern matching and detailed word tracking.
    /// </summary>
    private void AnalyzeTranscriptForResultStats(string transcript, SpeakerAttributionResult result)
    {
        if (string.IsNullOrEmpty(transcript)) return;

        // Split transcript into speaker segments
        string[] lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            // Look for speaker pattern: "Speaker: text"
            Match speakerMatch = Regex.Match(line, @"^([^:]+):\s*(.+)$");
            if (!speakerMatch.Success) continue;

            string speaker = speakerMatch.Groups[1].Value.Trim();
            string text = speakerMatch.Groups[2].Value.Trim();

            if (string.IsNullOrEmpty(text)) continue;

            // Count words
            int wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            result.WordCountsPerSpeaker[speaker] = result.WordCountsPerSpeaker.GetValueOrDefault(speaker, 0) + wordCount;
            result.TotalWords += wordCount;

            // Count questions and track phrases
            int questionCount = text.Count(c => c == '?');
            if (questionCount > 0)
            {
                result.QuestionCountsPerSpeaker[speaker] = result.QuestionCountsPerSpeaker.GetValueOrDefault(speaker, 0) + questionCount;
                result.TotalQuestions += questionCount;

                // Extract question phrases for detailed view
                string[] sentences = text.Split(new char[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string sentence in sentences)
                {
                    if (sentence.Contains('?'))
                    {
                        string questionPhrase = sentence.Trim() + "?";
                        if (!result.QuestionPhrasesPerSpeaker.ContainsKey(speaker))
                            result.QuestionPhrasesPerSpeaker[speaker] = new List<string>();
                        result.QuestionPhrasesPerSpeaker[speaker].Add(questionPhrase);
                    }
                }
            }

            // Enhanced laughter detection with word tracking
            MatchCollection laughterMatches = _wordAnalysisService.FindLaughter(text);
            if (laughterMatches.Count > 0)
            {
                result.LaughterCountsPerSpeaker[speaker] = result.LaughterCountsPerSpeaker.GetValueOrDefault(speaker, 0) + laughterMatches.Count;
                result.TotalLaughterMoments += laughterMatches.Count;

                // Track actual laughter words for detailed view
                if (!result.LaughterWordsPerSpeaker.ContainsKey(speaker))
                    result.LaughterWordsPerSpeaker[speaker] = new List<string>();

                foreach (Match match in laughterMatches)
                {
                    result.LaughterWordsPerSpeaker[speaker].Add(match.Value.ToLower());
                }
            }

            // Enhanced curse word detection with severity tracking
            (MatchCollection mildProfanityMatches, MatchCollection strongProfanityMatches) = _wordAnalysisService.FindAllProfanity(text);

            int totalCurseWords = mildProfanityMatches.Count + strongProfanityMatches.Count;
            if (totalCurseWords > 0)
            {
                result.CurseWordCountsPerSpeaker[speaker] = result.CurseWordCountsPerSpeaker.GetValueOrDefault(speaker, 0) + totalCurseWords;
                result.TotalCurseWords += totalCurseWords;

                // Track actual curse words for detailed view
                if (!result.CurseWordsPerSpeaker.ContainsKey(speaker))
                    result.CurseWordsPerSpeaker[speaker] = new List<string>();

                foreach (Match match in mildProfanityMatches)
                {
                    result.CurseWordsPerSpeaker[speaker].Add($"{match.Value.ToLower()} (mild)");
                }

                foreach (Match match in strongProfanityMatches)
                {
                    result.CurseWordsPerSpeaker[speaker].Add($"{match.Value.ToLower()} (strong)");
                }
            }

            // Pejorative and derogatory term detection
            MatchCollection pejorativeMatches = _wordAnalysisService.FindPejoratives(text);
            if (pejorativeMatches.Count > 0)
            {
                result.PejorativeCountsPerSpeaker[speaker] = result.PejorativeCountsPerSpeaker.GetValueOrDefault(speaker, 0) + pejorativeMatches.Count;
                result.TotalPejoratives += pejorativeMatches.Count;

                // Track actual pejorative words for detailed view
                if (!result.PejorativeWordsPerSpeaker.ContainsKey(speaker))
                    result.PejorativeWordsPerSpeaker[speaker] = new List<string>();

                foreach (Match match in pejorativeMatches)
                {
                    result.PejorativeWordsPerSpeaker[speaker].Add(match.Value.ToLower());
                }
            }
        }

        // Determine conversation tone
        result.ConversationTone = DetermineConversationTone(transcript);
    }

    /// <summary>
    /// Fixes speaker attribution by matching utterances between master mix and individual mic files.
    /// </summary>
    public async Task<SpeakerAttributionResult> FixSpeakerAttribution(string sessionPath, Dictionary<int, string> micAssignments)
    {
        SpeakerAttributionResult result = new();

        try
        {
            // Load master mix
            string masterMixPath = Path.Combine(sessionPath, "MASTER_MIX_transcription.json");
            if (!File.Exists(masterMixPath))
            {
                result.Success = false;
                result.ErrorMessage = "Master mix transcription file not found";
                return result;
            }

            GladiaTranscriptionResponse? masterData = await LoadTranscriptionFile(masterMixPath);
            if (masterData?.result?.transcription?.utterances == null)
            {
                result.Success = false;
                result.ErrorMessage = "Invalid master mix transcription data";
                return result;
            }

            // Load all available mic files
            Dictionary<int, List<WordCountUtterance>> micUtterances = new();
            for (int micNumber = 1; micNumber <= 6; micNumber++)
            {
                string micPath = Path.Combine(sessionPath, $"MIC{micNumber}_transcription.json");
                if (File.Exists(micPath))
                {
                    GladiaTranscriptionResponse? micData = await LoadTranscriptionFile(micPath);
                    if (micData?.result?.transcription?.utterances != null)
                    {
                        micUtterances[micNumber] = micData.result.transcription.utterances;
                        _logger.LogInformation("Loaded MIC{MicNumber} with {Count} utterances",
                            micNumber, micData.result.transcription.utterances.Count);
                    }
                }
            }

            // Process each utterance in master mix with enhanced matching for combined utterances
            List<EnhancedUtterance> enhancedUtterances = new();

            foreach (WordCountUtterance masterUtterance in masterData.result.transcription.utterances)
            {
                EnhancedUtterance enhanced = new()
                {
                    start = masterUtterance.start,
                    end = masterUtterance.end,
                    text = masterUtterance.text,
                    speaker = "Unknown",
                    speaker_number = 0,
                    confidence = masterUtterance.confidence,
                    words = masterUtterance.words,
                    matched_from_mic = null
                };

                // First try standard utterance-to-utterance matching
                (int? matchedMic, double matchScore, WordCountUtterance? bestMicUtterance) = FindBestMatchWithUtterance(masterUtterance, micUtterances);

                // If no good match found, try advanced matching for combined utterances
                if (matchScore < 0.5)
                {
                    (int? advancedMatchedMic, double advancedScore) = FindBestMatchForCombinedUtterance(masterUtterance, micUtterances);
                    if (advancedScore > matchScore)
                    {
                        matchedMic = advancedMatchedMic;
                        matchScore = advancedScore;
                        bestMicUtterance = null; // Combined matching doesn't return a single utterance
                    }
                }

                // Convert from 1-based mic file number to 0-based assignment key
                int assignmentKey = matchedMic.HasValue ? matchedMic.Value - 1 : -1;
                if (matchedMic.HasValue && micAssignments.TryGetValue(assignmentKey, out string? speakerName))
                {
                    enhanced.speaker = speakerName;
                    enhanced.speaker_number = assignmentKey; // Store the 0-based assignment key
                    enhanced.matched_from_mic = matchedMic.Value; // Store the 1-based mic file number for reference
                    enhanced.match_score = matchScore;
                    
                    // CRITICAL FIX: Trust individual mic transcription when it has higher quality
                    // If we found a specific mic utterance match and it has better confidence or text quality, use it
                    if (bestMicUtterance != null && ShouldUseMicTranscriptionInstead(masterUtterance, bestMicUtterance, matchScore))
                    {
                        enhanced.text = bestMicUtterance.text; // Use the mic's text instead of master mix
                        enhanced.confidence = bestMicUtterance.confidence; // Use mic's confidence
                        enhanced.words = bestMicUtterance.words; // Use mic's word-level timing
                        enhanced.start = bestMicUtterance.start; // Use mic's timing
                        enhanced.end = bestMicUtterance.end;
                        
                        _logger.LogDebug("Using mic {MicNumber} transcription instead of master mix for utterance at {Time}s - mic confidence: {MicConf}, master confidence: {MasterConf}",
                            matchedMic.Value, enhanced.start, bestMicUtterance.confidence, masterUtterance.confidence);
                    }
                    
                    result.MatchedUtterances++;
                    result.UtterancesPerPerson[speakerName] = result.UtterancesPerPerson.GetValueOrDefault(speakerName, 0) + 1;
                }
                else
                {
                    result.UnmatchedUtterances++;
                    result.UnmatchedTexts.Add($"[{masterUtterance.start:F2}-{masterUtterance.end:F2}] {masterUtterance.text}");
                }

                enhancedUtterances.Add(enhanced);
            }

            // Apply smart speaker deduction for remaining unknown utterances
            ApplySmartSpeakerDeduction(enhancedUtterances, micAssignments, result);

            // Create the enhanced transcription response
            EnhancedTranscriptionResponse enhancedResponse = new()
            {
                id = masterData.id,
                status = masterData.status,
                result = new EnhancedTranscriptionResult
                {
                    transcription = new EnhancedTranscriptionData
                    {
                        utterances = enhancedUtterances,
                        full_transcript = masterData.result.transcription.full_transcript
                    }
                },
                metadata = new TranscriptionMetadata
                {
                    original_file = "MASTER_MIX_transcription.json",
                    processing_date = DateTime.UtcNow,
                    mic_mappings = micAssignments,
                    statistics = new ProcessingStatistics
                    {
                        total_utterances = enhancedUtterances.Count,
                        matched_utterances = result.MatchedUtterances,
                        unmatched_utterances = result.UnmatchedUtterances,
                        utterances_per_person = result.UtterancesPerPerson,
                        mic_files_used = micUtterances.Keys.ToList()
                    }
                }
            };

            // Save the enhanced file
            string outputPath = Path.Combine(sessionPath, "master_mix_with_speakers.json");
            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(enhancedResponse, options);
            await File.WriteAllTextAsync(outputPath, json);

            // Generate conversation statistics from the corrected transcript
            await PopulateConversationStatisticsForResult(result, enhancedUtterances);

            result.Success = true;
            result.OutputFilePath = outputPath;
            result.TotalUtterances = enhancedUtterances.Count;

            _logger.LogInformation("Speaker attribution complete: {Matched} matched, {Unmatched} unmatched out of {Total} utterances",
                result.MatchedUtterances, result.UnmatchedUtterances, result.TotalUtterances);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fixing speaker attribution");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Fixes speaker attribution for a MovieSession using its mic assignments.
    /// This integrates into the audio processing workflow.
    /// </summary>
    public async Task<SpeakerAttributionResult> FixSpeakerAttributionForSession(MovieSession session)
    {
        if (session.MicAssignments == null || !session.MicAssignments.Any())
        {
            _logger.LogWarning("No mic assignments found in session {SessionId}, skipping speaker attribution fix", session.Id);
            return new SpeakerAttributionResult
            {
                Success = false,
                ErrorMessage = "No mic assignments available"
            };
        }

        return await FixSpeakerAttribution(session.FolderPath, session.MicAssignments);
    }

    /// <summary>
    /// Creates a corrected transcript that can be used for OpenAI analysis.
    /// Returns the enhanced transcript text with proper speaker names.
    /// </summary>
    public async Task<string> CreateCorrectedTranscriptForAI(MovieSession session)
    {
        try
        {
            SpeakerAttributionResult result = await FixSpeakerAttributionForSession(session);

            if (!result.Success || string.IsNullOrEmpty(result.OutputFilePath))
            {
                _logger.LogWarning("Speaker attribution fix failed, using original transcript");
                // Fall back to original master mix if available
                AudioFile? fallbackMasterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording && !string.IsNullOrEmpty(f.TranscriptText));
                return fallbackMasterFile?.TranscriptText ?? "No transcript available";
            }

            // Load the corrected JSON file
            string correctedJson = await File.ReadAllTextAsync(result.OutputFilePath);
            EnhancedTranscriptionResponse? enhanced = JsonSerializer.Deserialize<EnhancedTranscriptionResponse>(correctedJson);

            if (enhanced?.result?.transcription?.utterances != null)
            {
                // Build a formatted transcript for OpenAI with speaker names
                StringBuilder transcript = new();

                foreach (EnhancedUtterance utterance in enhanced.result.transcription.utterances)
                {
                    string speakerName = utterance.speaker;

                    // Handle unknown speakers with guidance
                    if (speakerName == "Unknown" && session.MicAssignments.Any())
                    {
                        // Provide context to help OpenAI make educated guesses
                        string availableNames = string.Join(", ", session.MicAssignments.Values);
                        speakerName = $"Unknown (likely one of: {availableNames})";
                    }

                    transcript.AppendLine($"{speakerName}: {utterance.text.Trim()}");
                }

                // Add metadata about speaker accuracy expectations
                StringBuilder finalTranscript = new();
                finalTranscript.AppendLine("=== SPEAKER ATTRIBUTION NOTES FOR AI ANALYSIS ===");
                finalTranscript.AppendLine("CRITICAL: The exact spelling of names is essential. Never modify these names:");

                foreach (var assignment in session.MicAssignments)
                {
                    finalTranscript.AppendLine($"- Mic {assignment.Key}: {assignment.Value} (ALWAYS use this exact spelling)");
                }

                finalTranscript.AppendLine();
                finalTranscript.AppendLine("When uncertain about speaker attribution, make educated guesses but ALWAYS:");
                finalTranscript.AppendLine("1. Use the EXACT spelling provided above");
                finalTranscript.AppendLine("2. Never create variations of names (e.g., don't say 'Carrie' when the name is 'Keri')");
                finalTranscript.AppendLine("3. Never use phonetic approximations (e.g., don't say 'Lacy' when the name is 'Lacey')");
                finalTranscript.AppendLine();
                finalTranscript.AppendLine("=== TRANSCRIPT BEGINS ===");
                finalTranscript.Append(transcript.ToString());

                return finalTranscript.ToString();
            }

            // Fallback to original if parsing fails
            _logger.LogWarning("Failed to parse enhanced transcript, using original");
            AudioFile? fallbackMasterFile2 = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording && !string.IsNullOrEmpty(f.TranscriptText));
            return fallbackMasterFile2?.TranscriptText ?? "No transcript available";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating corrected transcript for AI analysis");
            // Fallback to original transcript
            AudioFile? fallbackMasterFile3 = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording && !string.IsNullOrEmpty(f.TranscriptText));
            return fallbackMasterFile3?.TranscriptText ?? "No transcript available";
        }
    }

    /// <summary>
    /// Determines whether to use the mic transcription instead of master mix transcription.
    /// Mic transcription is preferred when it has higher confidence or significantly better text quality.
    /// </summary>
    private bool ShouldUseMicTranscriptionInstead(WordCountUtterance masterUtterance, WordCountUtterance micUtterance, double matchScore)
    {
        // Always use mic transcription if match confidence is very high (near perfect match)
        if (matchScore > 0.9)
        {
            return true;
        }
        
        // Use mic transcription if it has significantly higher confidence
        if (micUtterance.confidence > masterUtterance.confidence + 0.1) // 10% higher confidence threshold
        {
            return true;
        }
        
        // Use mic transcription if it's significantly longer (more detailed) and match quality is good
        if (matchScore > 0.7 && micUtterance.text.Length > masterUtterance.text.Length * 1.2) // 20% longer text
        {
            return true;
        }
        
        // Use mic transcription if master mix confidence is very low and mic confidence is reasonable
        if (masterUtterance.confidence < 0.5 && micUtterance.confidence > 0.7)
        {
            return true;
        }
        
        // Use mic transcription if it contains significantly more words (not just filler)
        string[] masterWords = masterUtterance.text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] micWords = micUtterance.text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        if (matchScore > 0.6 && micWords.Length > masterWords.Length * 1.5) // 50% more words
        {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Finds the best matching utterance from individual mic files and returns the actual utterance.
    /// Enhanced version that returns the matched utterance for potential text replacement.
    /// </summary>
    private (int? micNumber, double matchScore, WordCountUtterance? bestUtterance) FindBestMatchWithUtterance(
        WordCountUtterance masterUtterance,
        Dictionary<int, List<WordCountUtterance>> micUtterances)
    {
        int? bestMic = null;
        double bestScore = 0;
        WordCountUtterance? bestUtterance = null;
        const double timeWindowSeconds = 3.0; // Increased to 3 seconds for better tolerance

        foreach (KeyValuePair<int, List<WordCountUtterance>> kvp in micUtterances)
        {
            int micNumber = kvp.Key;
            List<WordCountUtterance> utterances = kvp.Value;

            foreach (WordCountUtterance micUtterance in utterances)
            {
                // More flexible timing check - see if master utterance falls within mic utterance timespan
                bool timeOverlap = DoTimespansOverlap(
                    masterUtterance.start, masterUtterance.end,
                    micUtterance.start, micUtterance.end,
                    timeWindowSeconds);

                if (timeOverlap)
                {
                    // Calculate text similarity
                    double textSimilarity = CalculateTextSimilarity(masterUtterance.text, micUtterance.text);

                    // Calculate timing similarity based on overlap
                    double timingSimilarity = CalculateTimingOverlap(
                        masterUtterance.start, masterUtterance.end,
                        micUtterance.start, micUtterance.end);

                    // Combined score with higher weight on text similarity since timing can vary
                    double score = (0.8 * textSimilarity) + (0.2 * timingSimilarity);

                    // Lower threshold since we're being more flexible
                    if (score > bestScore && score > 0.3)
                    {
                        bestScore = score;
                        bestMic = micNumber;
                        bestUtterance = micUtterance;
                    }
                }
            }
        }

        return (bestMic, bestScore, bestUtterance);
    }

    /// <summary>
    /// Finds the best matching utterance from individual mic files.
    /// Uses improved logic to handle cases where master mix splits utterances differently than mic files.
    /// Legacy method maintained for compatibility.
    /// </summary>
    private (int? micNumber, double matchScore) FindBestMatch(
        WordCountUtterance masterUtterance,
        Dictionary<int, List<WordCountUtterance>> micUtterances)
    {
        (int? micNumber, double matchScore, WordCountUtterance? _) = FindBestMatchWithUtterance(masterUtterance, micUtterances);
        return (micNumber, matchScore);
    }

    /// <summary>
    /// Advanced matching for combined utterances where master mix merges multiple mic utterances.
    /// Analyzes text content more deeply and considers multiple mic utterances as potential matches.
    /// </summary>
    private (int? micNumber, double matchScore) FindBestMatchForCombinedUtterance(
        WordCountUtterance masterUtterance,
        Dictionary<int, List<WordCountUtterance>> micUtterances)
    {
        int? bestMic = null;
        double bestScore = 0;
        const double timeWindowSeconds = 5.0; // Larger window for combined utterances

        foreach (KeyValuePair<int, List<WordCountUtterance>> kvp in micUtterances)
        {
            int micNumber = kvp.Key;
            List<WordCountUtterance> utterances = kvp.Value;

            // Find all mic utterances that overlap with this master utterance timespan
            List<WordCountUtterance> overlappingUtterances = utterances
                .Where(micUtterance => DoTimespansOverlap(
                    masterUtterance.start, masterUtterance.end,
                    micUtterance.start, micUtterance.end,
                    timeWindowSeconds))
                .ToList();

            if (overlappingUtterances.Any())
            {
                // Combine text from all overlapping utterances
                string combinedMicText = string.Join(" ", overlappingUtterances.Select(u => u.text.Trim()));

                // Calculate similarity between master text and combined mic text
                double textSimilarity = CalculateTextSimilarity(masterUtterance.text, combinedMicText);

                // Calculate timing overlap score based on how well the timespan aligns
                double timingScore = CalculateTimingOverlapForMultiple(masterUtterance, overlappingUtterances);

                // Calculate coverage score - how much of the master text is covered by mic utterances
                double coverageScore = CalculateCoverageScore(masterUtterance.text, overlappingUtterances);

                // Combined score emphasizing text similarity and coverage
                double score = (0.5 * textSimilarity) + (0.3 * coverageScore) + (0.2 * timingScore);

                if (score > bestScore && score > 0.4) // Lower threshold for combined matching
                {
                    bestScore = score;
                    bestMic = micNumber;
                }
            }
        }

        return (bestMic, bestScore);
    }

    /// <summary>
    /// Calculates timing overlap for multiple mic utterances against a master utterance.
    /// </summary>
    private double CalculateTimingOverlapForMultiple(WordCountUtterance masterUtterance, List<WordCountUtterance> micUtterances)
    {
        if (!micUtterances.Any())
            return 0;

        double masterStart = masterUtterance.start;
        double masterEnd = masterUtterance.end;
        double masterDuration = masterEnd - masterStart;

        // Find the span covered by mic utterances
        double micStart = micUtterances.Min(u => u.start);
        double micEnd = micUtterances.Max(u => u.end);

        // Calculate overlap
        double overlapStart = Math.Max(masterStart, micStart);
        double overlapEnd = Math.Min(masterEnd, micEnd);

        if (overlapEnd <= overlapStart)
            return 0;

        double overlapDuration = overlapEnd - overlapStart;
        return masterDuration > 0 ? Math.Min(1.0, overlapDuration / masterDuration) : 0;
    }

    /// <summary>
    /// Calculates how much of the master text is covered by the mic utterances.
    /// </summary>
    private double CalculateCoverageScore(string masterText, List<WordCountUtterance> micUtterances)
    {
        if (!micUtterances.Any() || string.IsNullOrWhiteSpace(masterText))
            return 0;

        string normalizedMaster = NormalizeText(masterText);
        HashSet<string> masterWords = new(normalizedMaster.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        HashSet<string> coveredWords = new();
        foreach (WordCountUtterance micUtterance in micUtterances)
        {
            string normalizedMicText = NormalizeText(micUtterance.text);
            string[] micWords = normalizedMicText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (string micWord in micWords)
            {
                if (masterWords.Contains(micWord))
                {
                    coveredWords.Add(micWord);
                }
            }
        }

        return masterWords.Count > 0 ? (double)coveredWords.Count / masterWords.Count : 0;
    }

    /// <summary>
    /// Checks if two timespans overlap within a tolerance window.
    /// </summary>
    private bool DoTimespansOverlap(double start1, double end1, double start2, double end2, double tolerance)
    {
        // Expand both timespans by tolerance
        double expandedStart1 = start1 - tolerance;
        double expandedEnd1 = end1 + tolerance;
        double expandedStart2 = start2 - tolerance;
        double expandedEnd2 = end2 + tolerance;

        // Check for any overlap
        return expandedStart1 <= expandedEnd2 && expandedStart2 <= expandedEnd1;
    }

    /// <summary>
    /// Calculates timing similarity based on overlap percentage.
    /// </summary>
    private double CalculateTimingOverlap(double start1, double end1, double start2, double end2)
    {
        double overlapStart = Math.Max(start1, start2);
        double overlapEnd = Math.Min(end1, end2);

        if (overlapEnd <= overlapStart)
            return 0; // No overlap

        double overlapDuration = overlapEnd - overlapStart;
        double duration1 = end1 - start1;
        double duration2 = end2 - start2;
        double minDuration = Math.Min(duration1, duration2);

        if (minDuration <= 0)
            return 0;

        return Math.Min(1.0, overlapDuration / minDuration);
    }

    /// <summary>
    /// Calculates similarity between two text strings (0-1).
    /// Improved to handle punctuation and spacing differences better.
    /// </summary>
    private double CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0;

        // Aggressive normalization to handle punctuation and spacing differences
        string normalized1 = NormalizeText(text1);
        string normalized2 = NormalizeText(text2);

        // If identical after normalization, return perfect score
        if (normalized1 == normalized2)
            return 1.0;

        // Extract meaningful words (filter out very short words)
        HashSet<string> words1 = new(normalized1.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)); // Filter out single character words
        HashSet<string> words2 = new(normalized2.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1));

        if (words1.Count == 0 || words2.Count == 0)
        {
            // If one has no meaningful words, check for substring containment
            return CheckSubstringMatch(normalized1, normalized2);
        }

        // Calculate Jaccard similarity (intersection over union)
        int intersection = words1.Intersect(words2).Count();
        int union = words1.Union(words2).Count();
        double jaccardSimilarity = union > 0 ? (double)intersection / union : 0;

        // Also check for partial word matches and substring containment
        double substringScore = CheckSubstringMatch(normalized1, normalized2);

        // Return the higher of the two scores
        return Math.Max(jaccardSimilarity, substringScore);
    }

    /// <summary>
    /// Normalizes text for better matching by removing punctuation and extra spaces.
    /// </summary>
    private string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Convert to lowercase and remove punctuation
        string normalized = text.ToLowerInvariant();

        // Remove common punctuation marks
        normalized = normalized.Replace(",", "")
                              .Replace(".", "")
                              .Replace("!", "")
                              .Replace("?", "")
                              .Replace(";", "")
                              .Replace(":", "")
                              .Replace("\"", "")
                              .Replace("'", "");

        // Normalize whitespace
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    /// <summary>
    /// Checks for substring matches between two normalized texts.
    /// </summary>
    private double CheckSubstringMatch(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0;

        // Check if shorter text is contained in longer text
        string shorter = text1.Length <= text2.Length ? text1 : text2;
        string longer = text1.Length > text2.Length ? text1 : text2;

        if (longer.Contains(shorter))
        {
            // Return score based on how much of the longer text the shorter one covers
            return (double)shorter.Length / longer.Length;
        }

        // Check for partial overlaps at word boundaries
        string[] words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int matchingWords = 0;
        foreach (string word1 in words1)
        {
            if (words2.Any(word2 => word1.Contains(word2) || word2.Contains(word1)))
            {
                matchingWords++;
            }
        }

        int totalWords = Math.Max(words1.Length, words2.Length);
        return totalWords > 0 ? (double)matchingWords / totalWords : 0;
    }

    /// <summary>
    /// Applies smart speaker deduction to map remaining "Unknown" utterances to unassigned participants.
    /// If there's only one person not found in mic assignments and there are unknown utterances, 
    /// deduce that they belong to that missing person.
    /// </summary>
    private void ApplySmartSpeakerDeduction(List<EnhancedUtterance> enhancedUtterances, Dictionary<int, string> micAssignments, SpeakerAttributionResult result)
    {
        try
        {
            // Find utterances that are still marked as "Unknown"
            List<EnhancedUtterance> unknownUtterances = enhancedUtterances.Where(u => u.speaker == "Unknown").ToList();
            
            if (!unknownUtterances.Any())
            {
                _logger.LogDebug("No unknown utterances found, skipping smart speaker deduction");
                return;
            }

            // Get all assigned participants
            HashSet<string> assignedParticipants = new(micAssignments.Values.Where(v => !string.IsNullOrWhiteSpace(v)));
            
            // Get all participants mentioned in the session (this would need to come from session data)
            // For now, we'll look for participants that have been matched to see if there's a pattern
            HashSet<string> matchedParticipants = new(result.UtterancesPerPerson.Keys);
            
            _logger.LogInformation("Smart speaker deduction: {UnknownCount} unknown utterances, {AssignedCount} assigned participants: {Assigned}, {MatchedCount} matched participants: {Matched}",
                unknownUtterances.Count, assignedParticipants.Count, string.Join(", ", assignedParticipants),
                matchedParticipants.Count, string.Join(", ", matchedParticipants));

            // If we have exactly one more participant in assignments than we have matched,
            // and that participant isn't already matched, assign unknown utterances to them
            List<string> unassignedParticipants = assignedParticipants.Except(matchedParticipants).ToList();
            
            if (unassignedParticipants.Count == 1)
            {
                string missingParticipant = unassignedParticipants.First();
                _logger.LogInformation("Smart deduction: Found exactly one unmatched participant '{MissingParticipant}', assigning {UnknownCount} unknown utterances to them",
                    missingParticipant, unknownUtterances.Count);

                // Assign all unknown utterances to this participant
                foreach (EnhancedUtterance unknownUtterance in unknownUtterances)
                {
                    unknownUtterance.speaker = missingParticipant;
                    unknownUtterance.match_score = 0.8; // High confidence for deduction
                    unknownUtterance.matched_from_mic = null; // No specific mic match, but deduced
                    
                    // Update statistics
                    result.MatchedUtterances++;
                    result.UnmatchedUtterances--;
                    result.UtterancesPerPerson[missingParticipant] = result.UtterancesPerPerson.GetValueOrDefault(missingParticipant, 0) + 1;
                }

                // Remove these from unmatched texts since they're now matched
                List<string> timesToRemove = unknownUtterances.Select(u => $"[{u.start:F2}-{u.end:F2}]").ToList();
                result.UnmatchedTexts.RemoveAll(text => timesToRemove.Any(time => text.StartsWith(time)));

                _logger.LogInformation("Smart deduction complete: Assigned {AssignedCount} utterances to {Participant}. New stats: {Matched} matched, {Unmatched} unmatched",
                    unknownUtterances.Count, missingParticipant, result.MatchedUtterances, result.UnmatchedUtterances);
            }
            else
            {
                _logger.LogInformation("Smart deduction not applicable: Found {UnassignedCount} unassigned participants, need exactly 1 for deduction", unassignedParticipants.Count);
                if (unassignedParticipants.Any())
                {
                    _logger.LogInformation("Unassigned participants: {Unassigned}", string.Join(", ", unassignedParticipants));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during smart speaker deduction");
        }
    }

    /// <summary>
    /// Loads a transcription file from disk.
    /// </summary>
    private async Task<GladiaTranscriptionResponse?> LoadTranscriptionFile(string path)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<GladiaTranscriptionResponse>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading transcription file: {Path}", path);
            return null;
        }
    }
}

#region Models

public class TranscriptionAnalysisReport
{
    public bool MasterMixFound { get; set; }
    public int MasterMixUtteranceCount { get; set; }
    public Dictionary<int, string> MicFilesFound { get; set; } = new();
    public Dictionary<int, int> MicFileUtteranceCounts { get; set; } = new();
    public Dictionary<int, bool> MicFileSpeakerAlwaysZero { get; set; } = new();
    public int TotalMicFilesFound { get; set; }
    public List<string> Errors { get; set; } = new();

    // Debug statistics for speaker attribution
    public Dictionary<string, int> WordCountsPerSpeaker { get; set; } = new();
    public Dictionary<string, int> UtteranceCountsPerSpeaker { get; set; } = new();
    public string ConversationTone { get; set; } = string.Empty;
    public int TotalWords { get; set; }
    public int TotalUtterances { get; set; }
}

public class SpeakerAttributionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputFilePath { get; set; }
    public int TotalUtterances { get; set; }
    public int MatchedUtterances { get; set; }
    public int UnmatchedUtterances { get; set; }
    public Dictionary<string, int> UtterancesPerPerson { get; set; } = new();
    public List<string> UnmatchedTexts { get; set; } = new();

    // Conversation statistics for debugging
    public Dictionary<string, int> WordCountsPerSpeaker { get; set; } = new();
    public Dictionary<string, int> QuestionCountsPerSpeaker { get; set; } = new();
    public Dictionary<string, int> LaughterCountsPerSpeaker { get; set; } = new();
    public Dictionary<string, int> CurseWordCountsPerSpeaker { get; set; } = new();
    public Dictionary<string, int> PejorativeCountsPerSpeaker { get; set; } = new();
    public string ConversationTone { get; set; } = string.Empty;
    public int TotalWords { get; set; }
    public int TotalQuestions { get; set; }
    public int TotalLaughterMoments { get; set; }
    public int TotalCurseWords { get; set; }
    public int TotalPejoratives { get; set; }

    // Detailed word tracking for expandable display
    public Dictionary<string, List<string>> LaughterWordsPerSpeaker { get; set; } = new();
    public Dictionary<string, List<string>> CurseWordsPerSpeaker { get; set; } = new();
    public Dictionary<string, List<string>> PejorativeWordsPerSpeaker { get; set; } = new();
    public Dictionary<string, List<string>> QuestionPhrasesPerSpeaker { get; set; } = new();
}

// Gladia response models
public class GladiaTranscriptionResponse
{
    public string id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public GladiaTranscriptionResult? result { get; set; }
}

public class GladiaTranscriptionResult
{
    public GladiaTranscriptionData? transcription { get; set; }
}

public class GladiaTranscriptionData
{
    public string full_transcript { get; set; } = string.Empty;
    public List<WordCountUtterance> utterances { get; set; } = new();
}

// Enhanced models with speaker names
public class EnhancedTranscriptionResponse
{
    public string id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public EnhancedTranscriptionResult? result { get; set; }
    public TranscriptionMetadata? metadata { get; set; }
}

public class EnhancedTranscriptionResult
{
    public EnhancedTranscriptionData? transcription { get; set; }
}

public class EnhancedTranscriptionData
{
    public string full_transcript { get; set; } = string.Empty;
    public List<EnhancedUtterance> utterances { get; set; } = new();
}

public class EnhancedUtterance : WordCountUtterance
{
    public new string speaker { get; set; } = "Unknown"; // Override with string type
    public int speaker_number { get; set; } // Original speaker number
    public int? matched_from_mic { get; set; }
    public double? match_score { get; set; }
}

public class TranscriptionMetadata
{
    public string original_file { get; set; } = string.Empty;
    public DateTime processing_date { get; set; }
    public Dictionary<int, string> mic_mappings { get; set; } = new();
    public ProcessingStatistics? statistics { get; set; }
}

public class ProcessingStatistics
{
    public int total_utterances { get; set; }
    public int matched_utterances { get; set; }
    public int unmatched_utterances { get; set; }
    public Dictionary<string, int> utterances_per_person { get; set; } = new();
    public List<int> mic_files_used { get; set; } = new();
}

#endregion
