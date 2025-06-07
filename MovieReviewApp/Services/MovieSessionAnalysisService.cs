using MovieReviewApp.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Services;


public class MovieSessionAnalysisService
{
    // Maximum transcript size to prevent OpenAI timeouts - balanced for 20-min processing
    private const int MAX_TRANSCRIPT_SIZE = 60000; // Balanced limit for longer timeout window

    // Truncation warning message
    private const string TRUNCATION_WARNING = "\n\n[TRANSCRIPT TRUNCATED DUE TO LENGTH - ANALYSIS BASED ON FIRST {0:N0} CHARACTERS FOR OPTIMAL PROCESSING]";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MovieSessionAnalysisService> _logger;
    private readonly SecretsManager _secretsManager;
    private readonly AudioClipService _audioClipService;
    private readonly DiscussionQuestionsService _discussionQuestionsService;
    private readonly string _openAiApiKey;

    public MovieSessionAnalysisService(HttpClient httpClient, IConfiguration configuration, SecretsManager secretsManager, AudioClipService audioClipService, DiscussionQuestionsService discussionQuestionsService, ILogger<MovieSessionAnalysisService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _secretsManager = secretsManager;
        _audioClipService = audioClipService;
        _discussionQuestionsService = discussionQuestionsService;
        _logger = logger;

        // Get API key from secrets manager
        _openAiApiKey = _secretsManager.GetSecret("OpenAI:ApiKey") ?? string.Empty;

        if (!string.IsNullOrEmpty(_openAiApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAiApiKey}");
            _logger.LogInformation("OpenAI service initialized with API key");
        }
        else
        {
            _logger.LogWarning("OpenAI service initialized without API key - analysis will be disabled");
        }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_openAiApiKey);

    public async Task<CategoryResults> AnalyzeSessionAsync(MovieSession session)
    {
        var results = await AnalyzeSessionsAsync(new[] { session });
        return results.First().categoryResults;
    }

    public async Task<List<(MovieSession session, CategoryResults categoryResults)>> AnalyzeSessionsAsync(IEnumerable<MovieSession> sessions)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("OpenAI API key not configured - cannot perform analysis");
        }

        var sessionList = sessions.ToList();
        _logger.LogInformation("Starting parallel analysis of {SessionCount} sessions", sessionList.Count);

        // Configure parallelism - adjust based on your OpenAI rate limits
        var maxConcurrency = Math.Min(sessionList.Count, 3); // Start with 3 concurrent requests
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var analysisResults = new List<(MovieSession session, CategoryResults categoryResults)>();
        var tasks = sessionList.Select(async session =>
        {
            await semaphore.WaitAsync();
            try
            {
                _logger.LogInformation("Starting analysis for session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);

                var categoryResults = await AnalyzeSingleSessionAsync(session);

                lock (analysisResults)
                {
                    analysisResults.Add((session, categoryResults));
                }

                _logger.LogInformation("Completed analysis for session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);
                return (session, categoryResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze session {SessionId} - {MovieTitle}", session.Id, session.MovieTitle);

                // Return empty results for failed sessions rather than failing the entire batch
                var emptyResults = new CategoryResults();
                lock (analysisResults)
                {
                    analysisResults.Add((session, emptyResults));
                }
                return (session, emptyResults);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("Completed parallel analysis of {SessionCount} sessions", sessionList.Count);
        return analysisResults.OrderBy(r => sessionList.IndexOf(r.session)).ToList();
    }

    private async Task<CategoryResults> AnalyzeSingleSessionAsync(MovieSession session)
    {
        // Combine all transcripts with speaker information
        var combinedTranscript = BuildCombinedTranscript(session);

        _logger.LogDebug("Built combined transcript for session {SessionId}: {Length} characters",
            session.Id, combinedTranscript?.Length ?? 0);

        if (string.IsNullOrEmpty(combinedTranscript))
        {
            var fileInfo = session.AudioFiles.Select(f => $"{f.FileName}: HasTranscript={!string.IsNullOrEmpty(f.TranscriptText)}, Status={f.ProcessingStatus}").ToList();
            _logger.LogWarning("No transcript content available for analysis. Audio files: {FileInfo}", string.Join(", ", fileInfo));
            throw new Exception("No transcript content available for analysis");
        }

        if (combinedTranscript.Length < 500)
        {
            _logger.LogWarning("Combined transcript is very short ({Length} chars) - OpenAI may not have enough content to analyze", combinedTranscript.Length);
        }

        // Create the analysis prompt based on processaudio.md specifications
        var analysisPrompt = await CreateAnalysisPromptAsync(session.MovieTitle, session.Date, session.ParticipantsPresent, combinedTranscript);

        // Log prompt size before sending to OpenAI
        _logger.LogDebug("Sending prompt to OpenAI: {PromptSize:N0} characters, {TranscriptSize:N0} transcript chars",
            analysisPrompt.Length, combinedTranscript.Length);

        // Call OpenAI to analyze the transcript
        var analysisResult = await CallOpenAIForAnalysisWithRetry(analysisPrompt);

        // Save OpenAI response to movie session folder
        await SaveOpenAIResponseAsync(session, analysisPrompt, analysisResult);

        // Parse the AI response into CategoryResults
        var categoryResults = ParseAnalysisResult(analysisResult);

        // Generate audio clips for highlights
        await GenerateAudioClipsAsync(session, categoryResults);

        return categoryResults;
    }

    private string ConvertJsonTranscriptToPlainText(string jsonTranscript, string speakerName, List<string> participantsPresent)
    {
        try
        {
            // Check if it's actually JSON
            if (!jsonTranscript.TrimStart().StartsWith("{") && !jsonTranscript.TrimStart().StartsWith("["))
            {
                // Not JSON, return as-is
                return jsonTranscript;
            }

            var transcriptData = JsonSerializer.Deserialize<TranscriptData>(jsonTranscript);
            var sb = new StringBuilder();

            if (transcriptData?.utterances != null)
            {
                foreach (var utterance in transcriptData.utterances)
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
    private string BuildCombinedTranscript(MovieSession session)
    {
        var transcriptBuilder = new StringBuilder();

        // Add session context (this stays the same size)
        transcriptBuilder.AppendLine("=== TRANSCRIPT ANALYSIS CONTEXT ===");
        transcriptBuilder.AppendLine($"Movie: {session.MovieTitle}");
        transcriptBuilder.AppendLine($"Date: {session.Date:yyyy-MM-dd}");
        transcriptBuilder.AppendLine($"Participants: {string.Join(", ", session.ParticipantsPresent)}");
        transcriptBuilder.AppendLine();

        // Get master recording and individual files
        var masterFile = session.AudioFiles.FirstOrDefault(f => f.IsMasterRecording && !string.IsNullOrEmpty(f.TranscriptText));
        var individualFiles = session.AudioFiles.Where(f => !f.IsMasterRecording && !string.IsNullOrEmpty(f.TranscriptText)).ToList();

        _logger.LogInformation("Building transcript from {TotalFiles} audio files: Master={MasterFile}, Individual={IndividualCount}",
            session.AudioFiles.Count,
            masterFile?.FileName ?? "None",
            individualFiles.Count);

        foreach (var file in session.AudioFiles)
        {
            _logger.LogDebug("Audio file {FileName}: IsMaster={IsMaster}, HasTranscript={HasTranscript}, TranscriptLength={Length}",
                file.FileName,
                file.IsMasterRecording,
                !string.IsNullOrEmpty(file.TranscriptText),
                file.TranscriptText?.Length ?? 0);
        }

        // Calculate available space for transcripts (reserve space for context and instructions)
        var contextSize = transcriptBuilder.Length;
        var maxTranscriptSpace = MAX_TRANSCRIPT_SIZE - contextSize - 2000; // Reserve 2K for instructions and truncation warnings

        // Strategy: Prioritize master recording to avoid duplication, fall back to individual mics if needed
        if (masterFile != null)
        {
            // Use master recording as primary source since it captures the full conversation
            _logger.LogInformation("Using master recording for analysis (size: {Size} chars)", masterFile.TranscriptText.Length);

            transcriptBuilder.AppendLine("=== MASTER RECORDING (Full Group Conversation) ===");
            transcriptBuilder.AppendLine("⚠️  IMPORTANT: Use ONLY timestamps from this master recording for audio clips!");
            transcriptBuilder.AppendLine("This captures everyone talking together with natural overlaps and interruptions.");
            transcriptBuilder.AppendLine("All audio clips will be generated from this file, so timestamps must match this timeline.");
            transcriptBuilder.AppendLine();

            var masterTranscript = masterFile.TranscriptText;

            // Convert JSON format to plain text
            masterTranscript = ConvertJsonTranscriptToPlainText(masterTranscript, "Speaker", session.ParticipantsPresent);

            if (masterTranscript.Length > maxTranscriptSpace)
            {
                // Smart truncation: keep beginning and end, skip middle
                var keepSize = maxTranscriptSpace - 500; // Reserve space for truncation message
                var beginningSize = (int)(keepSize * 0.6); // 60% from beginning
                var endingSize = keepSize - beginningSize; // 40% from end

                var beginning = masterTranscript.Substring(0, beginningSize);
                var ending = masterTranscript.Substring(masterTranscript.Length - endingSize);

                masterTranscript = beginning +
                    $"\n\n[TRANSCRIPT TRUNCATED - SHOWING FIRST {beginningSize:N0} AND LAST {endingSize:N0} CHARACTERS]\n" +
                    "[MIDDLE SECTION REMOVED FOR SIZE - FOCUS ON OPENING QUESTIONS AND KEY MOMENTS]\n\n" +
                    ending;

                _logger.LogWarning("Master recording smart-truncated from {Original} to {Truncated} characters (beginning + end)",
                    masterFile.TranscriptText.Length, masterTranscript.Length);
            }

            transcriptBuilder.AppendLine(masterTranscript);
            transcriptBuilder.AppendLine();
            transcriptBuilder.AppendLine("=== END MASTER RECORDING ===");
        }
        else if (individualFiles.Any())
        {
            // Fall back to individual mics only if no master recording available
            _logger.LogInformation("No master recording available, using {Count} individual mic recordings", individualFiles.Count);

            transcriptBuilder.AppendLine("=== INDIVIDUAL MIC RECORDINGS ===");
            transcriptBuilder.AppendLine("⚠️  WARNING: Do NOT use timestamps from individual mics for audio clips!");
            transcriptBuilder.AppendLine("These capture individual speakers clearly but timestamps may not align with master recording.");
            transcriptBuilder.AppendLine("Use these ONLY for speaker identification and quote verification.");
            transcriptBuilder.AppendLine();

            var remainingSpace = maxTranscriptSpace;
            var filesAdded = 0;

            foreach (var audioFile in individualFiles.OrderBy(f => f.SpeakerNumber ?? 99))
            {
                // Determine the speaker name based on mic number
                var participantIndex = (audioFile.SpeakerNumber ?? 1) - 1;
                var participantName = session.ParticipantsPresent.ElementAtOrDefault(participantIndex);

                if (participantName == null)
                {
                    _logger.LogWarning("No participant mapped for mic {MicNumber}, skipping", audioFile.SpeakerNumber);
                    continue;
                }

                var speakerLabel = participantName;
                var fileName = Path.GetFileNameWithoutExtension(audioFile.FilePath);
                var headerSize = $"--- {speakerLabel} ({fileName}) ---\n".Length;

                if (headerSize + 500 > remainingSpace) // Need at least 500 chars for meaningful content
                {
                    _logger.LogWarning("Stopping at {FilesAdded} individual files due to size limits", filesAdded);
                    transcriptBuilder.AppendLine($"\n[ADDITIONAL {individualFiles.Count - filesAdded} INDIVIDUAL MIC FILES SKIPPED DUE TO SIZE LIMITS]");
                    break;
                }

                transcriptBuilder.AppendLine($"--- {speakerLabel} ({fileName}) ---");
                remainingSpace -= headerSize;

                var transcript = audioFile.TranscriptText;

                // Convert JSON format to plain text using the participant's name
                transcript = ConvertJsonTranscriptToPlainText(transcript, participantName, session.ParticipantsPresent);

                if (transcript.Length > remainingSpace)
                {
                    transcript = transcript.Substring(0, Math.Max(0, remainingSpace - 100));
                    transcript += "\n[TRUNCATED]";
                    _logger.LogWarning("Individual file {FileName} truncated from {Original} to {Truncated} characters",
                        fileName, audioFile.TranscriptText.Length, transcript.Length);
                }

                transcriptBuilder.AppendLine(transcript);
                transcriptBuilder.AppendLine();
                remainingSpace -= transcript.Length + 2; // +2 for newlines
                filesAdded++;
            }
        }
        else
        {
            transcriptBuilder.AppendLine("=== NO TRANSCRIPT CONTENT AVAILABLE ===");
            _logger.LogWarning("No transcript content found for session {SessionId}", session.Id);
        }

        var finalTranscript = transcriptBuilder.ToString();
        _logger.LogInformation("Final combined transcript size: {Size} characters (max: {Max})",
            finalTranscript.Length, MAX_TRANSCRIPT_SIZE);

        return finalTranscript;
    }

    private async Task<string> CreateAnalysisPromptAsync(string movieTitle, DateTime sessionDate, List<string> participants, string transcript)
    {
        var participantsList = string.Join(", ", participants);
        var discussionQuestions = await GetFormattedDiscussionQuestionsAsync();
        var jsonSchema = GenerateJsonSchema();

        return $@"
You are analyzing a movie discussion group's recorded conversation for maximum entertainment value. This is a group of friends having an UNFILTERED discussion about ""{movieTitle}"" on {sessionDate:MMMM dd, yyyy}. Your job is to find the most outrageous, hilarious, and memorable moments - the stuff people will want to replay and share.

## PARTICIPANTS:
The following people were present for this discussion. These are their CORRECT names - please use these exact spellings when identifying speakers:

{participantsList}

**IMPORTANT**: When transcription software identifies speakers as ""Speaker1"", ""Speaker2"", etc. or misspells names like ""Gary"" or ""Carrie"" (when it should be ""Keri""), try to match them to the correct names above based on context and voice patterns. Use the exact spellings provided.

## CRITICAL SPEAKER VERIFICATION REQUIREMENTS:

**MASTER_MIX TRANSCRIPTIONS ARE OFTEN WRONG!** You MUST verify every speaker attribution by cross-referencing with individual microphone recordings.

### Speaker Verification Process:
1. **Microphone Mapping**: Individual mic files (mic1.wav, mic2.wav, etc.) correspond to participants in the EXACT order listed above:
   - mic1.wav = {participants.ElementAtOrDefault(0) ?? "First participant"}
   - mic2.wav = {participants.ElementAtOrDefault(1) ?? "Second participant"}
   - mic3.wav = {participants.ElementAtOrDefault(2) ?? "Third participant"}
   - etc.

2. **Verification Steps for EVERY Quote**:
   - Find the timestamp in master_mix where something interesting was said
   - Check that EXACT timestamp in ALL individual mic recordings
   - The clearest audio at that timestamp indicates the TRUE speaker
   - Individual mic files use ""speaker: 0"" - this just means the person on that mic
   - TRUST THE INDIVIDUAL MIC OVER MASTER_MIX

3. **Example**:
   - Master_mix at 123.45s shows: ""Dave: This movie sucked""
   - Check timestamp 123.45s in all individual files
   - If mic3.wav has clear audio saying ""This movie sucked"" at 123.45s
   - Then the third participant ({participants.ElementAtOrDefault(2) ?? "Third participant"}) said it
   - NOT Dave (unless Dave happens to be the third participant)

4. **Handling Variations**:
   - Minor word differences are acceptable: ""That's crazy"" vs ""That is crazy""
   - Different transcription services may hear things slightly differently
   - Focus on matching the core content and timestamp

5. **Overlapping Speech**:
   - When multiple people talk at once, ONLY include the main speaker's content
   - Remove side comments, ""yeah"", laughter, or unrelated interjections
   - Be STRICT about this - only keep directly relevant speech

6. **Quality Control**:
   - If you can't find a quote in ANY individual mic file, mark it as [UNVERIFIED]
   - Never guess based on ""who would say this"" - only use mic verification
   - Individual mics are the SOURCE OF TRUTH

## IMPORTANT TRANSCRIPT CONTEXT:
The transcript below contains multiple audio sources from the SAME conversation:
- Master_mix recording shows the full group dynamic
- Individual mic recordings (mic1, mic2, etc.) show specific people clearly
- All recordings are time-synchronized
- When master_mix says ""Speaker1"" or gets a name wrong, check individual mics!

**CRITICAL: For EVERY quote you include in your analysis:**
1. Note which individual mic file verified the speaker
2. Use the participant name from the ordered list above
3. Never trust master_mix speaker labels without verification

## YOUR MISSION:
Find the most entertaining moments in these categories:

### CONTROVERSIAL & SPICY TAKES
- Most Offensive Take
- Hottest Take 
- Biggest Argument Starter

### PEAK COMEDY GOLD
- Best Joke
- Best Roast
- Funniest Random Tangent

### GROUP DYNAMICS & REACTIONS
- Most Passionate Defense
- Biggest Unanimous Reaction
- Most Boring Statement
- Best Plot Twist Revelation

### PERSONALITY MOMENTS
- Movie Snob Moment
- Guilty Pleasure Admission
- Quietest Person's Best Moment

### TOP 5 LISTS
- Top 5 Funniest Sentences
- Top 5 Most Bland Comments

### INITIAL DISCUSSION QUESTIONS
Look for when these common discussion questions were asked and answered:
{discussionQuestions}

## CRITICAL: RESPONSE FORMAT
You MUST return your response as a valid JSON object matching this EXACT structure. Do not include any text before or after the JSON:

{jsonSchema}

## VERIFICATION CHECKLIST:
Before attributing ANY quote to ANYONE:
- ✓ Did I check the timestamp in all individual mic files?
- ✓ Did I find clear audio in a specific mic file?
- ✓ Did I map the mic number to the correct participant name?
- ✓ Am I ignoring what master_mix claims about who said it?

Remember: Master_mix helps you find interesting moments. Individual mics tell you WHO ACTUALLY SAID IT.

TRANSCRIPT TO ANALYZE:
{transcript}";
    }
    private async Task<string> CallOpenAIForAnalysisWithRetry(string prompt)
    {
        const int maxRetries = 3;
        var baseDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await CallOpenAIForAnalysis(prompt);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("rate limit"))
            {
                if (attempt == maxRetries)
                {
                    _logger.LogError("OpenAI rate limit exceeded after {MaxRetries} attempts", maxRetries);
                    throw;
                }

                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt - 1); // Exponential backoff
                _logger.LogWarning("OpenAI rate limit hit on attempt {Attempt}/{MaxRetries}, retrying in {DelayMs}ms",
                    attempt, maxRetries, delayMs);

                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI request failed on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt == maxRetries) throw;

                await Task.Delay(baseDelayMs * attempt);
            }
        }

        throw new Exception("Should not reach here");
    }

    private async Task<string> CallOpenAIForAnalysis(string prompt)
    {
        var requestBody = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = "You are an expert at finding the most entertaining, edgy, and memorable moments in friend group conversations. You understand that real friends can be crude, inappropriate, and brutally honest with each other - and that's what makes it entertaining. You're not looking for polite discussion, you're hunting for moments that make people laugh, gasp, or get fired up. Capture the unfiltered authenticity of friends being themselves." },
                new { role = "user", content = prompt }
            },
            max_tokens = 8000,
            temperature = 0.7
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var responseObj = JsonSerializer.Deserialize<OpenAIResponse>(responseJson);

        return responseObj?.choices?.FirstOrDefault()?.message?.content ??
               throw new Exception("No response content from OpenAI");
    }

    private CategoryResults ParseAnalysisResult(string analysisResult)
    {
        try
        {
            // Extract JSON from the response (in case there's additional text)
            var jsonMatch = Regex.Match(analysisResult, @"\{[\s\S]*\}", RegexOptions.Multiline);
            var jsonContent = jsonMatch.Success ? jsonMatch.Value : analysisResult;

            _logger.LogDebug("Parsing analysis result JSON: {JsonContent}", jsonContent.Substring(0, Math.Min(jsonContent.Length, 500)));

            // Try to parse as nested structure first, then fall back to flat structure
            var categoryResults = TryParseNestedStructure(jsonContent) ?? TryParseFlatStructure(jsonContent);

            if (categoryResults == null)
            {
                _logger.LogError("Failed to deserialize analysis data in both nested and flat formats");
                return new CategoryResults();
            }

            // Fix speaker name mappings
            ApplySpeakerNameMappings(categoryResults);

            return categoryResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse analysis result: {Result}", analysisResult);
            return new CategoryResults();
        }
    }

    private CategoryResults? TryParseNestedStructure(string jsonContent)
    {
        try
        {
            _logger.LogDebug("Attempting to parse nested JSON structure");

            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // Log the top-level sections found
            var topLevelSections = root.EnumerateObject().Select(p => p.Name).ToList();
            _logger.LogInformation("Found top-level JSON sections: {Sections}", string.Join(", ", topLevelSections));

            // Log detailed structure for debugging
            LogJsonStructure(root);

            var categoryResults = new CategoryResults();

            // Parse CONTROVERSIAL & SPICY TAKES section
            if (TryGetNestedCategory(root, "CONTROVERSIAL & SPICY TAKES", "Most Offensive Take", out var mostOffensive))
                categoryResults.MostOffensiveTake = ParseCategoryWinnerFromElement(mostOffensive);

            if (TryGetNestedCategory(root, "CONTROVERSIAL & SPICY TAKES", "Hottest Take", out var hottestTake))
                categoryResults.HottestTake = ParseCategoryWinnerFromElement(hottestTake);

            if (TryGetNestedCategory(root, "CONTROVERSIAL & SPICY TAKES", "Biggest Argument Starter", out var biggestArg))
                categoryResults.BiggestArgumentStarter = ParseCategoryWinnerFromElement(biggestArg);

            // Parse PEAK COMEDY GOLD section
            if (TryGetNestedCategory(root, "PEAK COMEDY GOLD", "Best Joke", out var bestJoke))
                categoryResults.BestJoke = ParseCategoryWinnerFromElement(bestJoke);

            if (TryGetNestedCategory(root, "PEAK COMEDY GOLD", "Best Roast", out var bestRoast))
                categoryResults.BestRoast = ParseCategoryWinnerFromElement(bestRoast);

            if (TryGetNestedCategory(root, "PEAK COMEDY GOLD", "Funniest Random Tangent", out var funniestTangent))
                categoryResults.FunniestRandomTangent = ParseCategoryWinnerFromElement(funniestTangent);

            // Parse GROUP DYNAMICS & REACTIONS section
            if (TryGetNestedCategory(root, "GROUP DYNAMICS & REACTIONS", "Most Passionate Defense", out var passionateDefense))
                categoryResults.MostPassionateDefense = ParseCategoryWinnerFromElement(passionateDefense);

            if (TryGetNestedCategory(root, "GROUP DYNAMICS & REACTIONS", "Biggest Unanimous Reaction", out var unanimousReaction))
                categoryResults.BiggestUnanimousReaction = ParseCategoryWinnerFromElement(unanimousReaction);

            if (TryGetNestedCategory(root, "GROUP DYNAMICS & REACTIONS", "Most Boring Statement", out var boringStatement))
                categoryResults.MostBoringStatement = ParseCategoryWinnerFromElement(boringStatement);

            if (TryGetNestedCategory(root, "GROUP DYNAMICS & REACTIONS", "Best Plot Twist Revelation", out var plotTwist))
                categoryResults.BestPlotTwistRevelation = ParseCategoryWinnerFromElement(plotTwist);

            // Parse PERSONALITY MOMENTS section
            if (TryGetNestedCategory(root, "PERSONALITY MOMENTS", "Movie Snob Moment", out var movieSnob))
                categoryResults.MovieSnobMoment = ParseCategoryWinnerFromElement(movieSnob);

            if (TryGetNestedCategory(root, "PERSONALITY MOMENTS", "Guilty Pleasure Admission", out var guiltyPleasure))
                categoryResults.GuiltyPleasureAdmission = ParseCategoryWinnerFromElement(guiltyPleasure);

            if (TryGetNestedCategory(root, "PERSONALITY MOMENTS", "Quietest Person's Best Moment", out var quietestPerson))
                categoryResults.QuietestPersonBestMoment = ParseCategoryWinnerFromElement(quietestPerson);

            // Parse TOP 5 LISTS section
            if (TryGetNestedCategory(root, "TOP 5 LISTS", "Top 5 Funniest Sentences", out var funniest) ||
                TryGetNestedCategory(root, "TOP 5 LISTS", "Funniest Sentences", out funniest))
                categoryResults.FunniestSentences = ParseTopFiveListFromElement(funniest);

            if (TryGetNestedCategory(root, "TOP 5 LISTS", "Top 5 Most Bland Comments", out var bland) ||
                TryGetNestedCategory(root, "TOP 5 LISTS", "Most Bland Comments", out bland))
                categoryResults.MostBlandComments = ParseTopFiveListFromElement(bland);

            // Parse INITIAL DISCUSSION QUESTIONS section
            if (TryGetNestedCategory(root, "INITIAL DISCUSSION QUESTIONS", "questions", out var questions) ||
                TryGetNestedCategory(root, "INITIAL DISCUSSION QUESTIONS", "Opening Questions & Answers", out questions) ||
                TryGetNestedCategory(root, "INITIAL DISCUSSION QUESTIONS", "Questions", out questions))
            {
                _logger.LogInformation("Found INITIAL DISCUSSION QUESTIONS with 'questions' property");
                var parsedQuestions = ParseInitialQuestionsFromElement(questions);
                _logger.LogInformation("Parsed {Count} initial questions", parsedQuestions?.Count ?? 0);
                categoryResults.InitialQuestions = parsedQuestions;
            }
            else if (root.TryGetProperty("INITIAL DISCUSSION QUESTIONS", out var discussionSection))
            {
                // Try parsing the entire section if individual questions property isn't found
                _logger.LogInformation("Found INITIAL DISCUSSION QUESTIONS section, parsing entire section");
                var parsedQuestions = ParseInitialQuestionsFromElement(discussionSection);
                _logger.LogInformation("Parsed {Count} initial questions from section", parsedQuestions?.Count ?? 0);
                categoryResults.InitialQuestions = parsedQuestions;
            }
            else
            {
                _logger.LogWarning("INITIAL DISCUSSION QUESTIONS section not found in JSON");

                // Try to find it with different casing or at root level
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name.Contains("Initial", StringComparison.OrdinalIgnoreCase) &&
                        property.Name.Contains("Question", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Found questions section with name: {PropertyName}", property.Name);
                        var parsedQuestions = ParseInitialQuestionsFromElement(property.Value);
                        _logger.LogInformation("Parsed {Count} initial questions from {PropertyName}", parsedQuestions?.Count ?? 0, property.Name);
                        categoryResults.InitialQuestions = parsedQuestions;
                        break;
                    }
                }
            }

            // Log summary of what was parsed
            var parsedCategories = new List<string>();
            if (categoryResults.MostOffensiveTake != null) parsedCategories.Add("MostOffensiveTake");
            if (categoryResults.HottestTake != null) parsedCategories.Add("HottestTake");
            if (categoryResults.BiggestArgumentStarter != null) parsedCategories.Add("BiggestArgumentStarter");
            if (categoryResults.BestJoke != null) parsedCategories.Add("BestJoke");
            if (categoryResults.BestRoast != null) parsedCategories.Add("BestRoast");
            if (categoryResults.FunniestRandomTangent != null) parsedCategories.Add("FunniestRandomTangent");
            if (categoryResults.MostPassionateDefense != null) parsedCategories.Add("MostPassionateDefense");
            if (categoryResults.BiggestUnanimousReaction != null) parsedCategories.Add("BiggestUnanimousReaction");
            if (categoryResults.MostBoringStatement != null) parsedCategories.Add("MostBoringStatement");
            if (categoryResults.BestPlotTwistRevelation != null) parsedCategories.Add("BestPlotTwistRevelation");
            if (categoryResults.MovieSnobMoment != null) parsedCategories.Add("MovieSnobMoment");
            if (categoryResults.GuiltyPleasureAdmission != null) parsedCategories.Add("GuiltyPleasureAdmission");
            if (categoryResults.QuietestPersonBestMoment != null) parsedCategories.Add("QuietestPersonBestMoment");
            if (categoryResults.FunniestSentences != null) parsedCategories.Add("FunniestSentences");
            if (categoryResults.MostBlandComments != null) parsedCategories.Add("MostBlandComments");
            if (categoryResults.InitialQuestions != null && categoryResults.InitialQuestions.Any()) parsedCategories.Add("InitialQuestions");

            _logger.LogInformation("Successfully parsed nested JSON structure. Categories found: {Categories}", string.Join(", ", parsedCategories));
            return categoryResults;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse nested structure, will try flat structure");
            return null;
        }
    }

    private CategoryResults? TryParseFlatStructure(string jsonContent)
    {
        try
        {
            _logger.LogDebug("Attempting to parse flat JSON structure");

            // Simply deserialize to the strongly typed model
            var response = JsonSerializer.Deserialize<OpenAIAnalysisResponse>(jsonContent);

            if (response == null)
            {
                _logger.LogError("Failed to deserialize flat analysis data - result was null");
                return null;
            }

            // Map from DTO to your domain model
            var categoryResults = MapToCategoryResults(response);

            // Extract initial questions to session stats (will be set later)
            if (response.OpeningQuestions != null)
            {
                // This will be handled in the session service when creating/updating the session
                categoryResults.InitialQuestions = MapInitialQuestions(response.OpeningQuestions);
            }

            _logger.LogInformation("Successfully parsed flat JSON structure");
            return categoryResults;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse flat structure as well");
            return null;
        }
    }

    private bool TryGetNestedCategory(JsonElement root, string sectionName, string categoryName, out JsonElement element)
    {
        element = default;

        // Try to find the section first
        if (root.TryGetProperty(sectionName, out var section))
        {
            _logger.LogDebug("Found section '{SectionName}', looking for category '{CategoryName}'", sectionName, categoryName);

            // Log what's in this section
            if (section.ValueKind == JsonValueKind.Object)
            {
                var sectionProperties = section.EnumerateObject().Select(p => p.Name).ToList();
                _logger.LogDebug("Section '{SectionName}' contains: {Properties}", sectionName, string.Join(", ", sectionProperties));
            }
            else if (section.ValueKind == JsonValueKind.Array)
            {
                _logger.LogDebug("Section '{SectionName}' is an array with {Count} elements", sectionName, section.GetArrayLength());
                // If the section itself is an array and we're looking for "questions", return it
                if (categoryName.Equals("questions", StringComparison.OrdinalIgnoreCase))
                {
                    element = section;
                    return true;
                }
            }

            // Then try to find the category within that section
            if (section.TryGetProperty(categoryName, out element))
            {
                _logger.LogDebug("Successfully found category '{CategoryName}' in section '{SectionName}'", categoryName, sectionName);
                return true;
            }

            // Also try with numbered keys like "1", "2", etc.
            for (int i = 1; i <= 20; i++)
            {
                if (section.TryGetProperty(i.ToString(), out var numberedCategory))
                {
                    // Check if this category matches what we're looking for by examining its content
                    if (IsCategoryMatch(numberedCategory, categoryName))
                    {
                        element = numberedCategory;
                        return true;
                    }
                }
            }
        }

        // Also try direct access at root level (in case structure is partially flat)
        if (root.TryGetProperty(categoryName, out element))
            return true;

        return false;
    }

    private bool IsCategoryMatch(JsonElement categoryElement, string expectedCategoryName)
    {
        // Try to identify the category by looking for key indicators in the content
        // This is a heuristic approach since OpenAI might use numbered keys

        if (categoryElement.ValueKind == JsonValueKind.Object)
        {
            // Look for a title or description field that might contain the category name
            foreach (var property in categoryElement.EnumerateObject())
            {
                if (property.Name.ToLower().Contains("title") ||
                    property.Name.ToLower().Contains("category") ||
                    property.Name.ToLower().Contains("type"))
                {
                    var value = property.Value.GetString() ?? "";
                    if (value.Contains(expectedCategoryName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }

        return false;
    }

    private CategoryWinner? ParseCategoryWinnerFromElement(JsonElement element)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;

            return new CategoryWinner
            {
                Speaker = GetStringProperty(element, "speaker") ?? "Unknown",
                Timestamp = GetStringProperty(element, "timestamp") ?? "0:00",
                Quote = GetStringProperty(element, "quote") ?? "",
                Setup = GetStringProperty(element, "setup") ?? "",
                GroupReaction = GetStringProperty(element, "groupReaction") ?? GetStringProperty(element, "group_reaction") ?? "",
                WhyItsGreat = GetStringProperty(element, "whyItsGreat") ?? GetStringProperty(element, "why_its_great") ?? "",
                AudioQuality = ParseAudioQuality(GetStringProperty(element, "audioQuality") ?? GetStringProperty(element, "audio_quality")),
                EntertainmentScore = GetIntProperty(element, "entertainmentScore") ?? GetIntProperty(element, "entertainment_score") ?? 5,
                RunnersUp = ParseRunnersUpFromElement(element.TryGetProperty("runnersUp", out var runnersUp) ? runnersUp :
                           element.TryGetProperty("runners_up", out var runnersUp2) ? runnersUp2 : default)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse category winner from element");
            return null;
        }
    }

    private TopFiveList? ParseTopFiveListFromElement(JsonElement element)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;

            var entriesElement = element.TryGetProperty("entries", out var entries) ? entries : element;

            if (entriesElement.ValueKind != JsonValueKind.Array)
                return null;

            var list = new TopFiveList();
            list.Entries = entriesElement.EnumerateArray()
                .Select(ParseTopFiveEntryFromElement)
                .Where(e => e != null)
                .Cast<TopFiveEntry>()
                .ToList();

            return list.Entries.Any() ? list : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse top five list from element");
            return null;
        }
    }

    private TopFiveEntry? ParseTopFiveEntryFromElement(JsonElement element)
    {
        try
        {
            return new TopFiveEntry
            {
                Rank = GetIntProperty(element, "rank") ?? 1,
                Speaker = GetStringProperty(element, "speaker") ?? "Unknown",
                Timestamp = GetStringProperty(element, "timestamp") ?? "0:00",
                Quote = GetStringProperty(element, "quote") ?? "",
                Context = GetStringProperty(element, "context") ?? "",
                AudioQuality = ParseAudioQuality(GetStringProperty(element, "audioQuality") ?? GetStringProperty(element, "audio_quality")),
                Score = GetDoubleProperty(element, "score") ?? 5.0,
                Reasoning = GetStringProperty(element, "reasoning") ?? "",
                StartTimeSeconds = GetDoubleArrayProperty(element, "estimatedStartEnd")?.ElementAtOrDefault(0),
                EndTimeSeconds = GetDoubleArrayProperty(element, "estimatedStartEnd")?.ElementAtOrDefault(1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse top five entry from element");
            return null;
        }
    }

    private List<QuestionAnswer> ParseInitialQuestionsFromElement(JsonElement element)
    {
        try
        {
            _logger.LogDebug("ParseInitialQuestionsFromElement called with element type: {ElementType}", element.ValueKind);

            // Handle different possible structures
            JsonElement questionsElement = element;

            // If the element is an object, look for a questions property
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("questions", out var questions))
                {
                    questionsElement = questions;
                    _logger.LogDebug("Found 'questions' property");
                }
                else if (element.TryGetProperty("Questions", out questions))
                {
                    questionsElement = questions;
                    _logger.LogDebug("Found 'Questions' property");
                }
                else
                {
                    // Check if the object directly contains numbered entries like "1", "2", etc.
                    var numberedQuestions = new List<QuestionAnswer>();
                    foreach (var property in element.EnumerateObject())
                    {
                        if (int.TryParse(property.Name, out _))
                        {
                            var qa = ParseQuestionAnswerFromElement(property.Value);
                            if (qa != null)
                            {
                                numberedQuestions.Add(qa);
                            }
                        }
                    }

                    if (numberedQuestions.Any())
                    {
                        _logger.LogInformation("Found {Count} numbered questions in object", numberedQuestions.Count);
                        return numberedQuestions;
                    }
                }
            }

            _logger.LogDebug("Questions element type: {ElementType}, IsArray: {IsArray}",
                questionsElement.ValueKind, questionsElement.ValueKind == JsonValueKind.Array);

            if (questionsElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Questions element is not an array, it's {ElementType}", questionsElement.ValueKind);

                // If it's an object, try to parse it as a single question
                if (questionsElement.ValueKind == JsonValueKind.Object)
                {
                    var singleQuestion = ParseQuestionAnswerFromElement(questionsElement);
                    if (singleQuestion != null)
                    {
                        _logger.LogInformation("Parsed single question from object");
                        return new List<QuestionAnswer> { singleQuestion };
                    }
                }

                return new List<QuestionAnswer>();
            }

            var arrayLength = questionsElement.GetArrayLength();
            _logger.LogInformation("Found {Length} questions in array", arrayLength);

            var results = questionsElement.EnumerateArray()
                .Select(ParseQuestionAnswerFromElement)
                .Where(qa => qa != null)
                .Cast<QuestionAnswer>()
                .ToList();

            _logger.LogInformation("Successfully parsed {Count} questions", results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse initial questions from element");
            return new List<QuestionAnswer>();
        }
    }

    private QuestionAnswer? ParseQuestionAnswerFromElement(JsonElement element)
    {
        try
        {
            return new QuestionAnswer
            {
                Question = GetStringProperty(element, "question") ?? "",
                Speaker = GetStringProperty(element, "speaker") ?? "",
                Answer = GetStringProperty(element, "answer") ?? "",
                Timestamp = GetStringProperty(element, "timestamp") ?? "",
                EntertainmentValue = GetIntProperty(element, "entertainmentValue") ?? GetIntProperty(element, "entertainment_value") ?? 5
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse question answer from element");
            return null;
        }
    }

    private List<RunnerUp> ParseRunnersUpFromElement(JsonElement element)
    {
        try
        {
            if (element.ValueKind != JsonValueKind.Array)
                return new List<RunnerUp>();

            return element.EnumerateArray()
                .Select(e => new RunnerUp
                {
                    Speaker = GetStringProperty(e, "speaker") ?? "Unknown",
                    Timestamp = GetStringProperty(e, "timestamp") ?? "0:00",
                    BriefDescription = GetStringProperty(e, "briefDescription") ?? GetStringProperty(e, "brief_description") ?? "",
                    Place = GetIntProperty(e, "place") ?? 2
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse runners up from element");
            return new List<RunnerUp>();
        }
    }

    // Helper methods for extracting properties with fallbacks
    private string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private int? GetIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var intValue))
                return intValue;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsedInt))
                return parsedInt;
        }
        return null;
    }

    private double? GetDoubleProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var doubleValue))
                return doubleValue;
            if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var parsedDouble))
                return parsedDouble;
        }
        return null;
    }

    private double[]? GetDoubleArrayProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            try
            {
                return prop.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Number)
                    .Select(e => e.GetDouble())
                    .ToArray();
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private CategoryResults MapToCategoryResults(OpenAIAnalysisResponse response)
    {
        return new CategoryResults
        {
            MostOffensiveTake = MapCategoryWinner(response.MostOffensiveTake),
            HottestTake = MapCategoryWinner(response.HottestTake),
            BiggestArgumentStarter = MapCategoryWinner(response.BiggestArgumentStarter),
            BestJoke = MapCategoryWinner(response.BestJoke),
            BestRoast = MapCategoryWinner(response.BestRoast),
            FunniestRandomTangent = MapCategoryWinner(response.FunniestRandomTangent),
            MostPassionateDefense = MapCategoryWinner(response.MostPassionateDefense),
            BiggestUnanimousReaction = MapCategoryWinner(response.BiggestUnanimousReaction),
            MostBoringStatement = MapCategoryWinner(response.MostBoringStatement),
            BestPlotTwistRevelation = MapCategoryWinner(response.BestPlotTwistRevelation),
            MovieSnobMoment = MapCategoryWinner(response.MovieSnobMoment),
            GuiltyPleasureAdmission = MapCategoryWinner(response.GuiltyPleasureAdmission),
            QuietestPersonBestMoment = MapCategoryWinner(response.QuietestPersonBestMoment),
            FunniestSentences = MapTopFiveList(response.Top5FunniestSentences),
            MostBlandComments = MapTopFiveList(response.Top5MostBlandComments)
        };
    }

    private CategoryWinner? MapCategoryWinner(CategoryWinnerDto? dto)
    {
        if (dto == null) return null;

        return new CategoryWinner
        {
            Speaker = dto.Speaker,
            Timestamp = dto.Timestamp,
            Quote = dto.Quote,
            Setup = dto.Setup,
            GroupReaction = dto.GroupReaction,
            WhyItsGreat = dto.WhyItsGreat,
            AudioQuality = ParseAudioQuality(dto.AudioQualityString),
            EntertainmentScore = dto.EntertainmentScore,
            RunnersUp = dto.RunnersUp.Select(r => new RunnerUp
            {
                Speaker = r.Speaker,
                Timestamp = r.Timestamp,
                BriefDescription = r.BriefDescription,
                Place = r.Place
            }).ToList()
        };
    }

    private TopFiveList? MapTopFiveList(TopFiveListDto? dto)
    {
        if (dto == null || !dto.Entries.Any()) return null;

        var list = new TopFiveList();
        list.Entries = dto.Entries.Select(e => new TopFiveEntry
        {
            Rank = e.Rank,
            Speaker = e.Speaker,
            Timestamp = e.Timestamp,
            Quote = e.Quote,
            Context = e.Context,
            AudioQuality = ParseAudioQuality(e.AudioQualityString),
            Score = e.Score,
            Reasoning = e.Reasoning,
            StartTimeSeconds = e.EstimatedStartEnd?.ElementAtOrDefault(0),
            EndTimeSeconds = e.EstimatedStartEnd?.ElementAtOrDefault(1)
        }).ToList();

        return list;
    }

    private List<QuestionAnswer> MapInitialQuestions(InitialQuestionsDto dto)
    {
        return dto.Questions.Select(q => new QuestionAnswer
        {
            Question = q.Question,
            Speaker = q.Speaker,
            Answer = q.Answer,
            Timestamp = q.Timestamp,
            EntertainmentValue = q.EntertainmentValue,
            AudioClipUrl = "" // Will be generated later if needed
        }).ToList();
    }

    private AudioQuality ParseAudioQuality(string? quality)
    {
        return quality?.ToLower() switch
        {
            "clear" => AudioQuality.Clear,
            "muffled" => AudioQuality.Muffled,
            "background noise" or "backgroundnoise" => AudioQuality.BackgroundNoise,
            _ => AudioQuality.Clear
        };
    }

    public SessionStats GenerateSessionStats(MovieSession session, CategoryResults categoryResults)
    {
        var stats = new SessionStats
        {
            TotalDuration = CalculateTotalDuration(session),
            TechnicalQuality = AssessTechnicalQuality(session),
            AttendancePattern = $"{session.ParticipantsPresent.Count}/{session.ParticipantsPresent.Count + session.ParticipantsAbsent.Count} regular members present"
        };

        // Calculate energy level based on categories found
        var highlightCount = CountHighlights(categoryResults);
        stats.HighlightMoments = highlightCount;

        stats.EnergyLevel = highlightCount switch
        {
            >= 10 => EnergyLevel.High,
            >= 5 => EnergyLevel.Medium,
            _ => EnergyLevel.Low
        };

        // Generate detailed conversation statistics
        GenerateConversationStatistics(session, stats);

        // Transfer initial questions from category results to session stats
        _logger.LogInformation("Transferring {Count} initial questions from CategoryResults to SessionStats", categoryResults.InitialQuestions?.Count ?? 0);
        stats.InitialQuestions = categoryResults.InitialQuestions;

        stats.BestMomentsSummary = GenerateBestMomentsSummary(categoryResults, stats.EnergyLevel);

        return stats;
    }

    private string CalculateTotalDuration(MovieSession session)
    {
        var maxDuration = session.AudioFiles
            .Where(f => f.Duration.HasValue)
            .Select(f => f.Duration!.Value.TotalMinutes)
            .DefaultIfEmpty(0)
            .Max();

        if (maxDuration >= 60)
        {
            var hours = (int)(maxDuration / 60);
            var minutes = (int)(maxDuration % 60);
            return $"{hours}h {minutes}m";
        }

        return $"{(int)maxDuration}m";
    }

    private string AssessTechnicalQuality(MovieSession session)
    {
        var totalFiles = session.AudioFiles.Count;
        if (totalFiles == 0) return "Unknown";

        var clearFiles = session.AudioFiles.Count(f => !string.IsNullOrEmpty(f.TranscriptText));
        var percentage = (double)clearFiles / totalFiles * 100;

        return percentage switch
        {
            >= 90 => "Excellent - all audio clear",
            >= 70 => "Good - most audio clear",
            >= 50 => "Fair - some audio issues",
            _ => "Poor - significant audio problems"
        };
    }

    private int CountHighlights(CategoryResults results)
    {
        var count = 0;

        if (results.BestJoke != null) count++;
        if (results.HottestTake != null) count++;
        if (results.BiggestArgumentStarter != null) count++;
        if (results.BestRoast != null) count++;
        if (results.FunniestRandomTangent != null) count++;
        if (results.MostPassionateDefense != null) count++;
        if (results.BiggestUnanimousReaction != null) count++;
        if (results.MostBoringStatement != null) count++;
        if (results.BestPlotTwistRevelation != null) count++;
        if (results.MovieSnobMoment != null) count++;
        if (results.GuiltyPleasureAdmission != null) count++;
        if (results.QuietestPersonBestMoment != null) count++;
        if (results.MostOffensiveTake != null) count++;
        if (results.FunniestSentences?.Entries.Any() == true) count += results.FunniestSentences.Entries.Count;
        if (results.MostBlandComments?.Entries.Any() == true) count += results.MostBlandComments.Entries.Count;

        return count;
    }

    private string GenerateBestMomentsSummary(CategoryResults results, EnergyLevel energyLevel)
    {
        var highlights = new List<string>();
        var spiceLevel = 0;

        if (results.BestJoke != null) { highlights.Add("comedy gold"); spiceLevel++; }
        if (results.BiggestArgumentStarter != null) { highlights.Add("heated drama"); spiceLevel += 2; }
        if (results.HottestTake != null) { highlights.Add("controversial hot takes"); spiceLevel += 2; }
        if (results.BestRoast != null) { highlights.Add("brutal roasts"); spiceLevel += 2; }
        if (results.FunniestRandomTangent != null) { highlights.Add("chaotic tangents"); spiceLevel++; }
        if (results.MostOffensiveTake != null) { highlights.Add("offensive commentary"); spiceLevel += 3; }
        if (results.MostPassionateDefense != null) { highlights.Add("passionate rants"); spiceLevel += 2; }

        var energyDescription = energyLevel switch
        {
            EnergyLevel.High when spiceLevel >= 8 => "🔥 ABSOLUTE CHAOS with",
            EnergyLevel.High when spiceLevel >= 5 => "🚀 Wild energy featuring",
            EnergyLevel.High => "⚡ High-octane discussion with",
            EnergyLevel.Medium when spiceLevel >= 5 => "🌶️ Spicy conversation featuring",
            EnergyLevel.Medium => "💬 Solid discussion with",
            EnergyLevel.Low when spiceLevel >= 3 => "😴 Sleepy but surprisingly featured",
            EnergyLevel.Low => "🤫 Chill session with",
            _ => "📝 Discussion featuring"
        };

        if (highlights.Any())
        {
            var joinedHighlights = highlights.Count switch
            {
                1 => highlights[0],
                2 => $"{highlights[0]} and {highlights[1]}",
                >= 3 => $"{string.Join(", ", highlights.Take(highlights.Count - 1))}, and {highlights.Last()}"
            };

            var enthusiasm = spiceLevel switch
            {
                >= 10 => " This one's going in the hall of fame! 🏆",
                >= 7 => " Definitely replay worthy! 🎬",
                >= 4 => " Some solid entertainment value here. 👌",
                _ => ""
            };

            return $"{energyDescription} {joinedHighlights}.{enthusiasm}";
        }

        return energyLevel switch
        {
            EnergyLevel.High => "🤷‍♂️ High energy but surprisingly wholesome discussion. Weird.",
            EnergyLevel.Medium => "📚 Civilized movie discussion. How refreshing... and boring.",
            EnergyLevel.Low => "😴 Sleepy session. Someone check for pulses.",
            _ => "🎬 Standard movie chat. Nothing to see here."
        };
    }

    private void GenerateConversationStatistics(MovieSession session, SessionStats stats)
    {
        try
        {
            _logger.LogInformation("Generating conversation statistics for {ParticipantCount} participants: {Participants}",
                session.ParticipantsPresent.Count, string.Join(", ", session.ParticipantsPresent));

            // Initialize dictionaries for all participants
            foreach (var participant in session.ParticipantsPresent)
            {
                stats.WordCounts[participant] = 0;
                stats.QuestionCounts[participant] = 0;
                stats.InterruptionCounts[participant] = 0;
                stats.LaughterCounts[participant] = 0;
                stats.CurseWordCounts[participant] = 0;
                _logger.LogDebug("Initialized stats for participant: '{Participant}'", participant);
            }

            // Analyze transcripts for conversation patterns
            var transcriptFiles = session.AudioFiles.Where(f => !string.IsNullOrEmpty(f.TranscriptText)).ToList();
            _logger.LogInformation("Analyzing {FileCount} transcript files for conversation patterns", transcriptFiles.Count);

            foreach (var audioFile in transcriptFiles)
            {
                _logger.LogDebug("Analyzing transcript from {FileName} with {Length} characters",
                    audioFile.FileName, audioFile.TranscriptText?.Length ?? 0);

                if (!string.IsNullOrEmpty(audioFile.TranscriptText))
                {
                    // Log first few lines of transcript for debugging
                    var firstLines = audioFile.TranscriptText.Split('\n').Take(5);
                    _logger.LogDebug("First few lines of {FileName}: {Lines}",
                        audioFile.FileName, string.Join(" | ", firstLines));
                }

                AnalyzeTranscriptForStats(audioFile.TranscriptText, stats, session.ParticipantsPresent);
            }

            // Calculate summary statistics
            if (stats.WordCounts.Any())
            {
                var mostTalkative = stats.WordCounts.OrderByDescending(kvp => kvp.Value).First();
                stats.MostTalkativePerson = $"{mostTalkative.Key} ({mostTalkative.Value:N0} words)";

                var quietest = stats.WordCounts.OrderBy(kvp => kvp.Value).First();
                stats.QuietestPerson = $"{quietest.Key} ({quietest.Value:N0} words)";

                _logger.LogInformation("Word counts: {WordCounts}",
                    string.Join(", ", stats.WordCounts.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
            }

            if (stats.QuestionCounts.Any())
            {
                var mostInquisitive = stats.QuestionCounts.OrderByDescending(kvp => kvp.Value).First();
                stats.MostInquisitivePerson = $"{mostInquisitive.Key} ({mostInquisitive.Value} questions)";
                stats.TotalQuestions = stats.QuestionCounts.Values.Sum();
            }

            if (stats.InterruptionCounts.Any())
            {
                var biggestInterruptor = stats.InterruptionCounts.OrderByDescending(kvp => kvp.Value).First();
                stats.BiggestInterruptor = $"{biggestInterruptor.Key} ({biggestInterruptor.Value} interruptions)";
                stats.TotalInterruptions = stats.InterruptionCounts.Values.Sum();
            }

            if (stats.LaughterCounts.Any())
            {
                var funniest = stats.LaughterCounts.OrderByDescending(kvp => kvp.Value).First();
                stats.FunniestPerson = $"{funniest.Key} ({funniest.Value} laughs triggered)";
                stats.TotalLaughterMoments = stats.LaughterCounts.Values.Sum();
            }

            if (stats.CurseWordCounts.Any())
            {
                var mostProfane = stats.CurseWordCounts.OrderByDescending(kvp => kvp.Value).First();
                stats.MostProfanePerson = $"{mostProfane.Key} ({mostProfane.Value} curse words)";
                stats.TotalCurseWords = stats.CurseWordCounts.Values.Sum();

                _logger.LogInformation("Curse word counts: {CurseWordCounts}",
                    string.Join(", ", stats.CurseWordCounts.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
            }

            // Determine conversation tone
            var totalWords = stats.WordCounts.Values.Sum();
            var wordsPerMinute = CalculateWordsPerMinute(session, totalWords);

            stats.ConversationTone = wordsPerMinute switch
            {
                > 300 => "Rapid-fire chaos",
                > 200 => "Animated discussion",
                > 150 => "Steady conversation",
                > 100 => "Relaxed chat",
                _ => "Contemplative silence"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate conversation statistics for session {SessionId}", session.Id);
        }
    }

    private void AnalyzeTranscriptForStats(string transcript, SessionStats stats, List<string> participants)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            _logger.LogWarning("AnalyzeTranscriptForStats called with empty transcript");
            return;
        }

        var lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var processedLines = 0;
        var totalLines = lines.Length;

        _logger.LogInformation("Analyzing transcript with {TotalLines} lines for participants: {Participants}",
            totalLines, string.Join(", ", participants));

        foreach (var line in lines)
        {
            // Skip timestamp lines and metadata
            if (line.Contains("[") || line.Contains("===") || line.Trim().Length < 10)
                continue;

            // Try to identify speaker from line format "Name: text" or "Speaker1: text"
            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1) continue;

            var speakerPart = line.Substring(0, colonIndex).Trim();
            var textPart = line.Substring(colonIndex + 1).Trim();

            // Skip if text part is empty or too short
            if (string.IsNullOrWhiteSpace(textPart) || textPart.Length < 2)
                continue;

            // Map speaker names (handle common transcription issues)
            var speaker = MapSpeakerName(speakerPart);

            // IMPORTANT: Check if this speaker is in our participants list
            var matchedParticipant = participants.FirstOrDefault(p =>
                p.Equals(speaker, StringComparison.OrdinalIgnoreCase) ||
                p.Contains(speaker, StringComparison.OrdinalIgnoreCase) ||
                speaker.Contains(p, StringComparison.OrdinalIgnoreCase));

            if (matchedParticipant == null)
            {
                // This speaker is NOT in our participants list - skip them entirely
                _logger.LogTrace("Skipping speaker '{Speaker}' - not in participants list", speaker);
                continue;
            }

            // Use the exact participant name from the list
            speaker = matchedParticipant;
            processedLines++;

            // Count words
            var words = textPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            stats.WordCounts[speaker] += words.Length;

            // Count questions
            var questionCount = textPart.Count(c => c == '?');
            stats.QuestionCounts[speaker] += questionCount;

            // Look for interruption patterns
            if (textPart.Contains("--") || textPart.Contains("[interrupting]") ||
                textPart.Contains("[talking over") || textPart.Contains("interrupts") ||
                textPart.Contains("cuts off") || textPart.Contains("overlapping"))
            {
                stats.InterruptionCounts[speaker]++;
            }

            // Look for laughter triggers
            var lowerText = textPart.ToLower();
            if (lowerText.Contains("laugh") || lowerText.Contains("haha") ||
                lowerText.Contains("[laughter]") || lowerText.Contains("funny") ||
                lowerText.Contains("lol") || lowerText.Contains("hilarious") ||
                lowerText.Contains("joke") || lowerText.Contains("chuckle"))
            {
                stats.LaughterCounts[speaker]++;
            }

            // Count curse words
            var curseWords = CountCurseWords(textPart);
            stats.CurseWordCounts[speaker] += curseWords;
        }

        _logger.LogInformation("=== TRANSCRIPT ANALYSIS COMPLETE ===");
        _logger.LogInformation("Processed {ProcessedLines}/{TotalLines} lines from transcript", processedLines, totalLines);
        _logger.LogInformation("FINAL WORD COUNTS: {WordCounts}",
            string.Join(", ", stats.WordCounts.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
    }

    private string MapSpeakerName(string rawSpeaker)
    {
        // Clean up the speaker name first
        var cleanSpeaker = rawSpeaker.Trim();

        // Handle common Gladia patterns like "Speaker 1", "speaker 1", etc.
        if (cleanSpeaker.StartsWith("Speaker ", StringComparison.OrdinalIgnoreCase))
        {
            // Keep as-is, will be matched by similarity in the calling method
            return cleanSpeaker;
        }

        // Handle numbered patterns like "Speaker1", "speaker1", etc.
        if (System.Text.RegularExpressions.Regex.IsMatch(cleanSpeaker, @"^Speaker\s*\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return cleanSpeaker;
        }

        // Handle common transcription patterns and name variations
        var nameMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Carrie", "Keri" },
            { "Gary", "Keri" },
            { "Kerry", "Keri" },
            { "Jerry", "Jeremiah" },
            { "Jeremy", "Jeremiah" },
            { "Lacy", "Lacey" }
        };

        return nameMapping.TryGetValue(cleanSpeaker, out var mapped) ? mapped : cleanSpeaker;
    }

    private double CalculateWordsPerMinute(MovieSession session, int totalWords)
    {
        var maxDuration = session.AudioFiles
            .Where(f => f.Duration.HasValue)
            .Select(f => f.Duration!.Value.TotalMinutes)
            .DefaultIfEmpty(1)
            .Max();

        return totalWords / Math.Max(maxDuration, 1);
    }

    private int CountCurseWords(string text)
    {
        // Common curse words and variations to detect
        var curseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "shit", "fuck", "fucking", "fucked", "fucker", "damn", "damned", "hell",
            "ass", "asshole", "bitch", "bastard", "crap", "piss", "bullshit",
            "motherfucker", "son of a bitch", "goddamn", "jesus christ", "christ",
            "wtf", "omfg", "ffs", "jfc", // common abbreviations
            "dammit", "goddammit", "bloody hell", "holy shit", "what the fuck",
            "piece of shit", "full of shit", "no shit", "holy fuck"
        };

        var words = text.ToLower()
            .Split(new char[] { ' ', '.', ',', '!', '?', ';', ':', '"', '\'', '-', '(', ')', '[', ']' },
                   StringSplitOptions.RemoveEmptyEntries);

        var count = 0;

        // Check individual words
        foreach (var word in words)
        {
            var cleanWord = word.Trim();
            if (curseWords.Contains(cleanWord))
            {
                count++;
            }
        }

        // Check for multi-word phrases
        var lowerText = text.ToLower();
        var phrases = new[] { "son of a bitch", "piece of shit", "full of shit", "holy shit",
                             "what the fuck", "jesus christ", "goddamn it", "bloody hell",
                             "holy fuck", "what the hell" };

        foreach (var phrase in phrases)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(lowerText, phrase);
            count += matches.Count;
        }

        return count;
    }

    private void ApplySpeakerNameMappings(CategoryResults categoryResults)
    {
        // Apply speaker name corrections
        var nameMapping = new Dictionary<string, string>
        {
            { "Carrie", "Keri" },
            { "Gary", "Keri" },
            { "Kerry", "Keri" },
            { "Jerry", "Jeremiah" },
            { "Jeremy", "Jeremiah" },
            { "Lacy", "Lacey" }
        };

        // Helper method to fix speaker names
        string FixSpeakerName(string? speaker)
        {
            if (string.IsNullOrEmpty(speaker)) return speaker ?? "";
            return nameMapping.TryGetValue(speaker, out var correctedName) ? correctedName : speaker;
        }

        // Fix category winners
        if (categoryResults.BestJoke != null) categoryResults.BestJoke.Speaker = FixSpeakerName(categoryResults.BestJoke.Speaker);
        if (categoryResults.HottestTake != null) categoryResults.HottestTake.Speaker = FixSpeakerName(categoryResults.HottestTake.Speaker);
        if (categoryResults.BiggestArgumentStarter != null) categoryResults.BiggestArgumentStarter.Speaker = FixSpeakerName(categoryResults.BiggestArgumentStarter.Speaker);
        if (categoryResults.BestRoast != null) categoryResults.BestRoast.Speaker = FixSpeakerName(categoryResults.BestRoast.Speaker);
        if (categoryResults.FunniestRandomTangent != null) categoryResults.FunniestRandomTangent.Speaker = FixSpeakerName(categoryResults.FunniestRandomTangent.Speaker);
        if (categoryResults.MostPassionateDefense != null) categoryResults.MostPassionateDefense.Speaker = FixSpeakerName(categoryResults.MostPassionateDefense.Speaker);
        if (categoryResults.BiggestUnanimousReaction != null) categoryResults.BiggestUnanimousReaction.Speaker = FixSpeakerName(categoryResults.BiggestUnanimousReaction.Speaker);
        if (categoryResults.MostBoringStatement != null) categoryResults.MostBoringStatement.Speaker = FixSpeakerName(categoryResults.MostBoringStatement.Speaker);
        if (categoryResults.BestPlotTwistRevelation != null) categoryResults.BestPlotTwistRevelation.Speaker = FixSpeakerName(categoryResults.BestPlotTwistRevelation.Speaker);
        if (categoryResults.MovieSnobMoment != null) categoryResults.MovieSnobMoment.Speaker = FixSpeakerName(categoryResults.MovieSnobMoment.Speaker);
        if (categoryResults.GuiltyPleasureAdmission != null) categoryResults.GuiltyPleasureAdmission.Speaker = FixSpeakerName(categoryResults.GuiltyPleasureAdmission.Speaker);
        if (categoryResults.QuietestPersonBestMoment != null) categoryResults.QuietestPersonBestMoment.Speaker = FixSpeakerName(categoryResults.QuietestPersonBestMoment.Speaker);
        if (categoryResults.MostOffensiveTake != null) categoryResults.MostOffensiveTake.Speaker = FixSpeakerName(categoryResults.MostOffensiveTake.Speaker);

        // Fix Top 5 lists
        if (categoryResults.FunniestSentences?.Entries != null)
        {
            foreach (var entry in categoryResults.FunniestSentences.Entries)
            {
                entry.Speaker = FixSpeakerName(entry.Speaker);
            }
        }

        if (categoryResults.MostBlandComments?.Entries != null)
        {
            foreach (var entry in categoryResults.MostBlandComments.Entries)
            {
                entry.Speaker = FixSpeakerName(entry.Speaker);
            }
        }

        // Fix runners up in all categories
        void FixRunnersUp(CategoryWinner? winner)
        {
            if (winner?.RunnersUp != null)
            {
                foreach (var runnerUp in winner.RunnersUp)
                {
                    runnerUp.Speaker = FixSpeakerName(runnerUp.Speaker);
                }
            }
        }

        FixRunnersUp(categoryResults.BestJoke);
        FixRunnersUp(categoryResults.HottestTake);
        FixRunnersUp(categoryResults.BiggestArgumentStarter);
        FixRunnersUp(categoryResults.BestRoast);
        FixRunnersUp(categoryResults.FunniestRandomTangent);
        FixRunnersUp(categoryResults.MostPassionateDefense);
        FixRunnersUp(categoryResults.BiggestUnanimousReaction);
        FixRunnersUp(categoryResults.MostBoringStatement);
        FixRunnersUp(categoryResults.BestPlotTwistRevelation);
        FixRunnersUp(categoryResults.MovieSnobMoment);
        FixRunnersUp(categoryResults.GuiltyPleasureAdmission);
        FixRunnersUp(categoryResults.QuietestPersonBestMoment);
        FixRunnersUp(categoryResults.MostOffensiveTake);
    }

    private async Task GenerateAudioClipsAsync(MovieSession session, CategoryResults categoryResults)
    {
        try
        {
            _logger.LogInformation("Starting audio clip generation for session {SessionId}", session.Id);

            // Generate clips for category winners that have timestamps
            await GenerateClipForCategoryWinner(session, categoryResults.BestJoke, "best-joke");
            await GenerateClipForCategoryWinner(session, categoryResults.HottestTake, "hottest-take");
            await GenerateClipForCategoryWinner(session, categoryResults.BestRoast, "best-roast");
            await GenerateClipForCategoryWinner(session, categoryResults.BiggestArgumentStarter, "biggest-argument");
            await GenerateClipForCategoryWinner(session, categoryResults.FunniestRandomTangent, "funniest-tangent");
            await GenerateClipForCategoryWinner(session, categoryResults.MostPassionateDefense, "passionate-defense");
            await GenerateClipForCategoryWinner(session, categoryResults.BiggestUnanimousReaction, "unanimous-reaction");
            await GenerateClipForCategoryWinner(session, categoryResults.BestPlotTwistRevelation, "plot-twist");
            await GenerateClipForCategoryWinner(session, categoryResults.MostOffensiveTake, "offensive-take");
            await GenerateClipForCategoryWinner(session, categoryResults.GuiltyPleasureAdmission, "guilty-pleasure");
            await GenerateClipForCategoryWinner(session, categoryResults.QuietestPersonBestMoment, "quietest-person");
            await GenerateClipForCategoryWinner(session, categoryResults.MovieSnobMoment, "movie-snob");

            // Generate clips for Top 5 lists
            if (categoryResults.FunniestSentences != null)
            {
                await _audioClipService.GenerateClipsForTopFiveAsync(session, categoryResults.FunniestSentences);
            }

            if (categoryResults.MostBlandComments != null)
            {
                await _audioClipService.GenerateClipsForTopFiveAsync(session, categoryResults.MostBlandComments);
            }

            // Generate clips for initial questions
            await GenerateClipsForInitialQuestions(session, categoryResults.InitialQuestions);

            _logger.LogInformation("Completed audio clip generation for session {SessionId}", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate audio clips for session {SessionId}", session.Id);
            // Don't throw - clip generation is optional and shouldn't fail the analysis
        }
    }

    private async Task GenerateClipForCategoryWinner(MovieSession session, CategoryWinner? winner, string clipPrefix)
    {
        if (winner == null || string.IsNullOrEmpty(winner.Timestamp))
            return;

        try
        {
            // Parse timestamp to find the right audio file and time
            var timestamp = _audioClipService.ParseTimestamp(winner.Timestamp);

            // Find the best audio file for this timestamp
            var sourceFile = FindBestAudioFileForTimestamp(session, timestamp);
            if (sourceFile == null)
            {
                _logger.LogWarning("No suitable audio file found for timestamp {Timestamp} in session {SessionId}",
                    winner.Timestamp, session.Id);
                return;
            }

            var clipId = $"{clipPrefix}_{Guid.NewGuid():N}";

            // Add padding around the timestamp (3 seconds before, 5 seconds after for context)
            double startTime = Math.Max(0, timestamp.TotalSeconds - 3);
            double endTime = timestamp.TotalSeconds + 5;

            var clipUrl = await _audioClipService.GenerateAudioClipAsync(
                sourceFile.FilePath,
                startTime,
                endTime,
                session.Id.ToString(),
                clipId);

            if (!string.IsNullOrEmpty(clipUrl))
            {
                winner.AudioClipUrl = clipUrl;
                _logger.LogDebug("Generated clip for {ClipPrefix} at {Timestamp}: {ClipUrl}",
                    clipPrefix, winner.Timestamp, clipUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate clip for {ClipPrefix} at {Timestamp}",
                clipPrefix, winner.Timestamp);
        }
    }

    private AudioFile? FindBestAudioFileForTimestamp(MovieSession session, TimeSpan timestamp)
    {
        // ALWAYS use master recording for audio clips to ensure consistency
        // The UI specifically expects clips from the master_mix file
        var masterFile = session.AudioFiles.FirstOrDefault(f =>
            f.IsMasterRecording &&
            f.Duration.HasValue &&
            f.Duration.Value >= timestamp);

        if (masterFile != null)
        {
            _logger.LogDebug("Using master recording {FileName} for clip at {Timestamp}",
                masterFile.FileName, timestamp);
            return masterFile;
        }

        _logger.LogWarning("No master recording found or master recording too short for timestamp {Timestamp}. " +
                          "Available files: {Files}",
            timestamp,
            string.Join(", ", session.AudioFiles.Select(f => $"{f.FileName} (Master: {f.IsMasterRecording}, Duration: {f.Duration})")));

        // Only fall back to individual files if absolutely no master recording exists
        // This should be rare since we typically have a master_mix file
        return session.AudioFiles
            .Where(f => f.Duration.HasValue && f.Duration.Value >= timestamp)
            .OrderByDescending(f => f.Duration)
            .FirstOrDefault();
    }

    private async Task GenerateClipsForInitialQuestions(MovieSession session, List<QuestionAnswer> questions)
    {
        if (!questions.Any()) return;

        try
        {
            for (int i = 0; i < questions.Count; i++)
            {
                var qa = questions[i];
                if (string.IsNullOrEmpty(qa.Timestamp)) continue;

                var timestamp = _audioClipService.ParseTimestamp(qa.Timestamp);
                var sourceFile = FindBestAudioFileForTimestamp(session, timestamp);

                if (sourceFile == null) continue;

                var clipId = $"initial-q{i + 1}_{qa.Speaker.ToLower()}_{Guid.NewGuid():N}";

                // Add padding around the answer (2 seconds before, 8 seconds after for full context)
                double startTime = Math.Max(0, timestamp.TotalSeconds - 2);
                double endTime = timestamp.TotalSeconds + 8;

                var clipUrl = await _audioClipService.GenerateAudioClipAsync(
                    sourceFile.FilePath,
                    startTime,
                    endTime,
                    session.Id.ToString(),
                    clipId);

                if (!string.IsNullOrEmpty(clipUrl))
                {
                    qa.AudioClipUrl = clipUrl;
                    _logger.LogDebug("Generated clip for initial question '{Question}' answered by {Speaker}: {ClipUrl}",
                        qa.Question, qa.Speaker, clipUrl);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate clips for initial questions in session {SessionId}", session.Id);
        }
    }

    private void LogJsonStructure(JsonElement root, string prefix = "", int maxDepth = 3, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth) return;

        try
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    var currentPrefix = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        _logger.LogDebug("JSON Structure: {Path} (Object)", currentPrefix);
                        LogJsonStructure(property.Value, currentPrefix, maxDepth, currentDepth + 1);
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        _logger.LogDebug("JSON Structure: {Path} (Array with {Count} elements)", currentPrefix, property.Value.GetArrayLength());

                        // Log structure of first array element if it exists
                        if (property.Value.GetArrayLength() > 0)
                        {
                            var firstElement = property.Value.EnumerateArray().First();
                            LogJsonStructure(firstElement, $"{currentPrefix}[0]", maxDepth, currentDepth + 1);
                        }
                    }
                    else
                    {
                        var valuePreview = property.Value.ValueKind == JsonValueKind.String ?
                            $"\"{property.Value.GetString()?.Substring(0, Math.Min(property.Value.GetString()?.Length ?? 0, 50)) ?? ""}...\"" :
                            property.Value.ToString();
                        _logger.LogDebug("JSON Structure: {Path} = {Value}", currentPrefix, valuePreview);
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                _logger.LogDebug("JSON Structure: {Path} (Array with {Count} elements)", prefix, root.GetArrayLength());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log JSON structure at {Path}", prefix);
        }
    }

    private async Task<string> GetFormattedDiscussionQuestionsAsync()
    {
        try
        {
            var questions = await _discussionQuestionsService.GetActiveQuestionsAsync();
            if (!questions.Any())
            {
                // Fallback to default questions if none configured
                return @"1. ""Did I like the movie?"" (or similar variations like ""Did you like it?"")
2. ""Am I glad I watched the movie?"" (or ""Are you glad you watched it?"")
3. ""Do I think I'd ever watch it again?"" (or ""Would you watch it again?"")
4. ""Would you ever recommend this movie?"" (or ""Would you recommend it?"")
5. ""What was my favorite part of the movie?"" (or ""What was your favorite part?"")
6. ""What was my least favorite part of the movie?"" (or ""What was your least favorite part?"")
7. ""What was my favorite line of the movie?"" (or ""What was your favorite line?"")";
            }

            var formattedQuestions = questions
                .Select((q, index) => $"{index + 1}. \"\"{q.Question}\"\" (or similar variations)")
                .ToList();

            return string.Join("\n", formattedQuestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get discussion questions for prompt, using defaults");
            // Fallback to default questions
            return @"1. ""Did I like the movie?"" (or similar variations like ""Did you like it?"")
2. ""Am I glad I watched the movie?"" (or ""Are you glad you watched it?"")
3. ""Do I think I'd ever watch it again?"" (or ""Would you watch it again?"")
4. ""Would you ever recommend this movie?"" (or ""Would you recommend it?"")
5. ""What was my favorite part of the movie?"" (or ""What was your favorite part?"")
6. ""What was my least favorite part of the movie?"" (or ""What was your least favorite part?"")
7. ""What was my favorite line of the movie?"" (or ""What was your favorite line?"")";
        }
    }

    private async Task SaveOpenAIResponseAsync(MovieSession session, string prompt, string response)
    {
        try
        {
            if (string.IsNullOrEmpty(session.FolderPath))
            {
                _logger.LogWarning("Cannot save OpenAI response - session folder path is empty for session {SessionId}", session.Id);
                return;
            }

            // Create the response data structure
            var responseData = new
            {
                SessionId = session.Id,
                MovieTitle = session.MovieTitle,
                Date = session.Date,
                ParticipantsPresent = session.ParticipantsPresent,
                ProcessedAt = DateTime.UtcNow,
                PromptSize = prompt.Length,
                ResponseSize = response.Length,
                Prompt = prompt,
                Response = response,
                Model = "gpt-4o-mini",
                Timeout = "20 minutes",
                MaxTranscriptSize = MAX_TRANSCRIPT_SIZE
            };

            // Create filename with timestamp
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var fileName = $"openai_analysis_{timestamp}.json";
            var filePath = Path.Combine(session.FolderPath, fileName);

            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            // Serialize and save to file
            var json = JsonSerializer.Serialize(responseData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(filePath, json);

            _logger.LogInformation("Saved OpenAI response to {FilePath} ({FileSize:N0} characters)",
                filePath, json.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save OpenAI response for session {SessionId}", session.Id);
            // Don't throw - this is optional functionality and shouldn't break the analysis
        }
    }

    private string GenerateJsonSchema()
    {
        var sampleResponse = new OpenAIAnalysisResponse
        {
            MostOffensiveTake = CreateSampleCategoryWinner(),
            HottestTake = CreateSampleCategoryWinner(),
            BiggestArgumentStarter = CreateSampleCategoryWinner(),
            BestJoke = CreateSampleCategoryWinner(),
            BestRoast = CreateSampleCategoryWinner(),
            FunniestRandomTangent = CreateSampleCategoryWinner(),
            MostPassionateDefense = CreateSampleCategoryWinner(),
            BiggestUnanimousReaction = CreateSampleCategoryWinner(),
            MostBoringStatement = CreateSampleCategoryWinner(),
            BestPlotTwistRevelation = CreateSampleCategoryWinner(),
            MovieSnobMoment = CreateSampleCategoryWinner(),
            GuiltyPleasureAdmission = CreateSampleCategoryWinner(),
            QuietestPersonBestMoment = CreateSampleCategoryWinner(),
            Top5FunniestSentences = CreateSampleTopFiveList(),
            Top5MostBlandComments = CreateSampleTopFiveList(),
            OpeningQuestions = CreateSampleInitialQuestions()
        };

        // Serialize with indentation to create a readable schema
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null, // Keep original property names
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        var schemaJson = JsonSerializer.Serialize(sampleResponse, options);

        // Replace sample values with placeholders and instructions
        schemaJson = ReplaceSampleValuesWithPlaceholders(schemaJson);

        return schemaJson;
    }

    private CategoryWinnerDto CreateSampleCategoryWinner()
    {
        return new CategoryWinnerDto
        {
            Speaker = "[Exact participant name]",
            Timestamp = "[MM:SS format]",
            Quote = "[Exact quote verified from individual mic]",
            Setup = "[Context leading to this moment]",
            GroupReaction = "[How others reacted]",
            WhyItsGreat = "[Why this is entertaining]",
            AudioQualityString = "Clear",
            EntertainmentScore = 8,
            RunnersUp = new List<RunnerUpDto>
            {
                new RunnerUpDto
                {
                    Speaker = "[Name]",
                    Timestamp = "[MM:SS]",
                    BriefDescription = "[Short description]",
                    Place = 2
                }
            }
        };
    }

    private TopFiveListDto CreateSampleTopFiveList()
    {
        return new TopFiveListDto
        {
            Entries = new List<TopFiveEntryDto>
            {
                new TopFiveEntryDto
                {
                    Rank = 1,
                    Speaker = "[Name]",
                    Timestamp = "[MM:SS]",
                    Quote = "[Quote]",
                    Context = "[Context]",
                    AudioQualityString = "Clear",
                    Score = 9.5,
                    Reasoning = "[Why this is funny/bland]"
                },
                new TopFiveEntryDto
                {
                    Rank = 2,
                    Speaker = "[Name]",
                    Timestamp = "[MM:SS]",
                    Quote = "[Quote]",
                    Context = "[Context]",
                    AudioQualityString = "Clear",
                    Score = 9.0,
                    Reasoning = "[Why this is funny/bland]"
                }
            }
        };
    }

    private InitialQuestionsDto CreateSampleInitialQuestions()
    {
        return new InitialQuestionsDto
        {
            Questions = new List<QuestionAnswerDto>
            {
                new QuestionAnswerDto
                {
                    Question = "[Question asked]",
                    Speaker = "[Who answered]",
                    Answer = "[Their answer]",
                    Timestamp = "[MM:SS]",
                    EntertainmentValue = 7
                }
            }
        };
    }

    private string ReplaceSampleValuesWithPlaceholders(string schemaJson)
    {
        // This method could be enhanced to replace specific patterns,
        // but for now the sample objects already contain placeholder text
        // that will guide OpenAI on what to put in each field

        return schemaJson;
    }
}

public class TranscriptData
{
    public List<WordCountUtterance> utterances { get; set; } = new List<WordCountUtterance>();
}

public class WordCountUtterance
{
    public double start { get; set; }
    public double end { get; set; }
    public string text { get; set; } = string.Empty;
    public int speaker { get; set; }
    public double confidence { get; set; }
    public List<WordCountWord>? words { get; set; }
}

public class WordCountWord
{
    public string word { get; set; } = string.Empty;
    public double start { get; set; }
    public double end { get; set; }
    public double confidence { get; set; }
}

// DTO for OpenAI analysis response structure
public class OpenAIAnalysisResponse
{
    [JsonPropertyName("Most Offensive Take")]
    public CategoryWinnerDto? MostOffensiveTake { get; set; }

    [JsonPropertyName("Hottest Take")]
    public CategoryWinnerDto? HottestTake { get; set; }

    [JsonPropertyName("Biggest Argument Starter")]
    public CategoryWinnerDto? BiggestArgumentStarter { get; set; }

    [JsonPropertyName("Best Joke")]
    public CategoryWinnerDto? BestJoke { get; set; }

    [JsonPropertyName("Best Roast")]
    public CategoryWinnerDto? BestRoast { get; set; }

    [JsonPropertyName("Funniest Random Tangent")]
    public CategoryWinnerDto? FunniestRandomTangent { get; set; }

    [JsonPropertyName("Most Passionate Defense")]
    public CategoryWinnerDto? MostPassionateDefense { get; set; }

    [JsonPropertyName("Biggest Unanimous Reaction")]
    public CategoryWinnerDto? BiggestUnanimousReaction { get; set; }

    [JsonPropertyName("Most Boring Statement")]
    public CategoryWinnerDto? MostBoringStatement { get; set; }

    [JsonPropertyName("Best Plot Twist Revelation")]
    public CategoryWinnerDto? BestPlotTwistRevelation { get; set; }

    [JsonPropertyName("Movie Snob Moment")]
    public CategoryWinnerDto? MovieSnobMoment { get; set; }

    [JsonPropertyName("Guilty Pleasure Admission")]
    public CategoryWinnerDto? GuiltyPleasureAdmission { get; set; }

    [JsonPropertyName("Quietest Person's Best Moment")]
    public CategoryWinnerDto? QuietestPersonBestMoment { get; set; }

    [JsonPropertyName("Top 5 Funniest Sentences")]
    public TopFiveListDto? Top5FunniestSentences { get; set; }

    [JsonPropertyName("Top 5 Most Bland Comments")]
    public TopFiveListDto? Top5MostBlandComments { get; set; }

    [JsonPropertyName("Opening Questions & Answers")]
    public InitialQuestionsDto? OpeningQuestions { get; set; }
}

public class CategoryWinnerDto
{
    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = "Unknown";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "0:00";

    [JsonPropertyName("quote")]
    public string Quote { get; set; } = "";

    [JsonPropertyName("setup")]
    public string Setup { get; set; } = "";

    [JsonPropertyName("groupReaction")]
    public string GroupReaction { get; set; } = "";

    [JsonPropertyName("whyItsGreat")]
    public string WhyItsGreat { get; set; } = "";

    [JsonPropertyName("audioQuality")]
    public string AudioQualityString { get; set; } = "Clear";

    [JsonPropertyName("entertainmentScore")]
    public int EntertainmentScore { get; set; } = 5;

    [JsonPropertyName("runnersUp")]
    public List<RunnerUpDto> RunnersUp { get; set; } = new();
}

public class RunnerUpDto
{
    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = "Unknown";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "0:00";

    [JsonPropertyName("briefDescription")]
    public string BriefDescription { get; set; } = "";

    [JsonPropertyName("place")]
    public int Place { get; set; } = 2;
}

public class TopFiveListDto
{
    [JsonPropertyName("entries")]
    public List<TopFiveEntryDto> Entries { get; set; } = new();
}

public class TopFiveEntryDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = "Unknown";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "0:00";

    [JsonPropertyName("quote")]
    public string Quote { get; set; } = "";

    [JsonPropertyName("context")]
    public string Context { get; set; } = "";

    [JsonPropertyName("audioQuality")]
    public string AudioQualityString { get; set; } = "Clear";

    [JsonPropertyName("score")]
    public double Score { get; set; } = 5.0;

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    [JsonPropertyName("estimatedStartEnd")]
    public double[]? EstimatedStartEnd { get; set; }
}

public class InitialQuestionsDto
{
    [JsonPropertyName("questions")]
    public List<QuestionAnswerDto> Questions { get; set; } = new();
}

public class QuestionAnswerDto
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = string.Empty;

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("entertainmentValue")]
    public int EntertainmentValue { get; set; } = 5;

    [JsonPropertyName("estimatedStartEnd")]
    public double[]? EstimatedStartEnd { get; set; }
}

// DTO for OpenAI API response
public class OpenAIResponse
{
    public OpenAIChoice[]? choices { get; set; }
}

public class OpenAIChoice
{
    public OpenAIMessage? message { get; set; }
}

public class OpenAIMessage
{
    public string? content { get; set; }
}