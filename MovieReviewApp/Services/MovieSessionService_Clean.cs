using System.Text.RegularExpressions;
using MovieReviewApp.Models;
using MovieReviewApp.Database;
using System.Text.Json;
using NAudio.Wave;

namespace MovieReviewApp.Services;

/// <summary>
/// Refactored MovieSessionService using the clean type-based database API
/// </summary>
public class MovieSessionService_Clean
{
    private readonly MongoDbService _database;
    private readonly GladiaService _gladiaService;
    private readonly MovieSessionAnalysisService _analysisService;
    private readonly AudioClipService _audioClipService;
    private readonly ILogger<MovieSessionService_Clean> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public MovieSessionService_Clean(
        MongoDbService database,
        GladiaService gladiaService,
        MovieSessionAnalysisService analysisService,
        AudioClipService audioClipService,
        ILogger<MovieSessionService_Clean> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _database = database;
        _gladiaService = gladiaService;
        _analysisService = analysisService;
        _audioClipService = audioClipService;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<MovieSession> CreateSessionFromFolder(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath);
        var match = Regex.Match(folderName, @"^(\d{4})-(\d{2})-(\d{2})_(.+)$");
        
        if (!match.Success)
        {
            throw new ArgumentException($"Invalid folder name format: {folderName}. Expected YYYY-MM-DD_MovieTitle");
        }

        var date = new DateTime(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value)
        );
        var movieTitle = match.Groups[4].Value.Replace("_", " ");

        var session = new MovieSession
        {
            Date = date,
            MovieTitle = movieTitle,
            FolderPath = folderPath,
            Status = ProcessingStatus.Pending
        };

        // Scan for audio files
        await ScanAudioFiles(session);
        
        // Determine participants based on files
        DetermineParticipants(session);

        // Save the session - CLEAN API!
        await _database.UpsertAsync(session);
        
        _logger.LogInformation("Created session {SessionId} for movie {MovieTitle}", session.Id, session.MovieTitle);
        
        return session;
    }

    public async Task ProcessSession(string sessionId, Action<ProcessingStatus, int, string>? progressCallback = null)
    {
        // CLEAN API - just pass the ID!
        var session = await _database.GetByIdAsync<MovieSession>(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        try
        {
            // Step 1: Validation
            progressCallback?.Invoke(ProcessingStatus.Validating, 10, "Validating session data");
            session.Status = ProcessingStatus.Validating;
            await _database.UpsertAsync(session); // CLEAN!

            if (!session.AudioFiles.Any())
            {
                throw new Exception("No audio files found in session");
            }

            // Step 2: Transcription
            progressCallback?.Invoke(ProcessingStatus.Transcribing, 20, "Starting transcription");
            session.Status = ProcessingStatus.Transcribing;
            await _database.UpsertAsync(session); // CLEAN!

            var audioFilePaths = session.AudioFiles.Select(f => f.FilePath).ToList();
            var transcriptionResults = await _gladiaService.ProcessMultipleFilesAsync(audioFilePaths, 
                (fileName, current, total) => 
                {
                    var progress = 20 + (int)((double)current / total * 50); // 20-70%
                    progressCallback?.Invoke(ProcessingStatus.Transcribing, progress, $"Transcribing {fileName}");
                });

            // Update audio files with transcription data
            for (int i = 0; i < session.AudioFiles.Count; i++)
            {
                var audioFile = session.AudioFiles[i];
                var transcriptionResult = transcriptionResults.FirstOrDefault(r => r.source_file_path == audioFile.FilePath);
                
                if (transcriptionResult?.result?.transcription?.full_transcript != null)
                {
                    audioFile.TranscriptId = transcriptionResult.id;
                    audioFile.TranscriptText = transcriptionResult.result.transcription.full_transcript;
                }
            }

            // Step 3: AI Analysis
            progressCallback?.Invoke(ProcessingStatus.Analyzing, 75, "Analyzing transcripts for entertainment moments");
            session.Status = ProcessingStatus.Analyzing;
            await _database.UpsertAsync(session); // CLEAN!

            await AnalyzeSession(session);

            // Step 4: Complete
            progressCallback?.Invoke(ProcessingStatus.Complete, 100, "Processing complete");
            session.Status = ProcessingStatus.Complete;
            session.ProcessedAt = DateTime.UtcNow;

            await _database.UpsertAsync(session); // CLEAN!
            
            _logger.LogInformation("Successfully processed session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session {SessionId}", sessionId);
            
            session.Status = ProcessingStatus.Failed;
            session.ErrorMessage = ex.Message;
            await _database.UpsertAsync(session); // CLEAN!
            
            throw;
        }
    }

    // Much cleaner public API methods!
    
    public async Task<List<MovieSession>> GetAllSessions()
    {
        return await _database.GetAllAsync<MovieSession>();
    }

    public async Task<List<MovieSession>> GetRecentSessions(int limit = 10)
    {
        // Even cleaner with paging support!
        var (sessions, _) = await _database.GetPagedAsync<MovieSession>(
            page: 1, 
            pageSize: limit,
            orderBy: s => s.CreatedAt,
            descending: true
        );
        
        return sessions;
    }

    public async Task<MovieSession?> GetSession(string sessionId)
    {
        return await _database.GetByIdAsync<MovieSession>(sessionId);
    }

    public async Task<bool> DeleteSession(string sessionId)
    {
        return await _database.DeleteByIdAsync<MovieSession>(sessionId);
    }

    public async Task<List<MovieSession>> SearchSessions(string searchTerm)
    {
        // Clean text search on multiple fields!
        return await _database.SearchTextAsync<MovieSession>(
            searchTerm, 
            s => s.MovieTitle,
            s => s.FolderPath
        );
    }

    public async Task<long> GetSessionCount()
    {
        return await _database.CountAsync<MovieSession>();
    }

    public async Task<bool> HasAnySessions()
    {
        return await _database.AnyAsync<MovieSession>();
    }

    public async Task<List<MovieSession>> GetSessionsByDateRange(DateTime start, DateTime end)
    {
        return await _database.FindAsync<MovieSession>(s => s.Date >= start && s.Date <= end);
    }

    public async Task<List<MovieSession>> GetFailedSessions()
    {
        return await _database.FindAsync<MovieSession>(s => s.Status == ProcessingStatus.Failed);
    }

    // Private methods remain the same...
    private async Task ScanAudioFiles(MovieSession session)
    {
        // Implementation unchanged
    }

    private void DetermineParticipants(MovieSession session)
    {
        // Implementation unchanged
    }

    private async Task AnalyzeSession(MovieSession session)
    {
        // Implementation unchanged
    }

    private async Task GenerateAudioClips(MovieSession session)
    {
        // Implementation unchanged
    }

    // etc...
}