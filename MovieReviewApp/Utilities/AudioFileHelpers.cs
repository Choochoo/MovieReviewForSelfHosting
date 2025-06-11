using MovieReviewApp.Models;
using System.Text.RegularExpressions;

namespace MovieReviewApp.Utilities;

/// <summary>
/// Shared utilities for audio file operations and naming conventions.
/// Eliminates code duplication across audio processing components.
/// </summary>
public static class AudioFileHelpers
{
    private static readonly string[] AllowedExtensions = { 
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma",
        ".mp4", ".mov", ".avi", ".mkv", ".webm", ".m4v", ".3gp"
    };

    /// <summary>
    /// Checks if a file is a supported audio/video file.
    /// </summary>
    public static bool IsAudioFile(string fileName)
    {
        return AllowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
    }

    /// <summary>
    /// Determines the file type based on naming conventions.
    /// </summary>
    public static string DetermineFileType(string fileName, Dictionary<int, string> micAssignments)
    {
        string upperName = fileName.ToUpper();
        
        // Check for MIC1-6 pattern (file names are 1-based, but we store assignments as 0-based)
        Match micMatch = Regex.Match(upperName, @"^MIC(\d)\.WAV$");
        if (micMatch.Success)
        {
            int micFileNum = int.Parse(micMatch.Groups[1].Value); // 1-based from file
            int micAssignmentNum = micFileNum - 1; // Convert to 0-based for assignment lookup
            string participantName = micAssignments.TryGetValue(micAssignmentNum, out string? name) 
                ? name 
                : $"Speaker {micFileNum}"; // Display uses 1-based
            return $"🎤 {participantName}";
        }
        
        // Check for PHONE
        if (upperName == "PHONE.WAV")
        {
            return "📞 Phone Input";
        }
        
        // Check for SOUND_PAD
        if (upperName == "SOUND_PAD.WAV" || upperName == "SOUNDPAD.WAV")
        {
            return "🔊 Sound Pad";
        }
        
        // Check for legacy speaker files (1-based file naming)
        Match speakerMatch = Regex.Match(fileName, @"^(\d)_Speaker\d", RegexOptions.IgnoreCase);
        if (speakerMatch.Success)
        {
            int speakerFileNum = int.Parse(speakerMatch.Groups[1].Value); // 1-based from file
            int speakerAssignmentNum = speakerFileNum - 1; // Convert to 0-based for assignment lookup
            string participantName = micAssignments.TryGetValue(speakerAssignmentNum, out string? name) 
                ? name 
                : $"Speaker {speakerFileNum}"; // Display uses 1-based
            return $"🎤 {participantName}";
        }
        
        // Check for master recording with date pattern (e.g., 2024_1122_1839.wav)
        Match datePattern = Regex.Match(fileName, @"^\d{4}_\d{4}_\d{4}\.(wav|mp3|m4a|aac|ogg|flac)$", RegexOptions.IgnoreCase);
        if (datePattern.Success)
        {
            return "🎬 Master Mix (timestamped)";
        }
        
        // Check for master recording with keywords
        string lowerName = fileName.ToLower();
        if (lowerName.Contains("master") || lowerName.Contains("combined") || 
            lowerName.Contains("full") || lowerName.Contains("group"))
        {
            return "🎬 Master Recording";
        }
        
        // If unidentified, just show the filename
        return fileName;
    }

    /// <summary>
    /// Sanitizes a filename by removing invalid characters.
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Join("", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Replace(" ", "-"); // Replace spaces with hyphens for cleaner folder names
    }

    /// <summary>
    /// Formats bytes in a human-readable format.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return string.Format("{0:n1} {1}", number, suffixes[counter]);
    }

    /// <summary>
    /// Checks if a file is a temporary file that should be ignored.
    /// </summary>
    public static bool IsTemporaryFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath).ToLowerInvariant();
        
        // Skip temporary files created during processing
        if (fileName.StartsWith("temp_") || fileName.Contains("_temp") || fileName.EndsWith(".tmp"))
            return true;
            
        // Skip duplicate master files (only show the renamed MASTER_MIX version)
        if (fileName.Contains("master") && !fileName.Equals("master_mix.wav", StringComparison.OrdinalIgnoreCase))
            return true;
            
        return false;
    }

    /// <summary>
    /// Gets the movie title from a folder path using naming convention.
    /// </summary>
    public static string GetMovieTitleFromPath(string path)
    {
        string folderName = Path.GetFileName(path);
        Match match = Regex.Match(folderName, @"^\d{4}-\d{2}-\d{2}_(.+)$");
        return match.Success ? match.Groups[1].Value.Replace("_", " ") : folderName;
    }

    /// <summary>
    /// Generates a folder name based on movie and date.
    /// </summary>
    public static string GenerateFolderName(string movieTitle, DateTime sessionDate)
    {
        if (string.IsNullOrWhiteSpace(movieTitle))
            return "YYYY-MonthName-MovieTitle";
        
        string sanitizedTitle = Regex.Replace(movieTitle.Trim(), @"[^a-zA-Z0-9\s-]", "")
                                     .Replace(" ", "-");
        return $"{sessionDate:yyyy}-{sessionDate:MMMM}-{sanitizedTitle}";
    }
}