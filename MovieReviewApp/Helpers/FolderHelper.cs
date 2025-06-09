using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace MovieReviewApp.Helpers
{
    public static class FolderHelper
    {
        public static string[] GetTextFromFiles(string folderPath)
        {

            // Ensure the folder exists
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"The folder '{folderPath}' does not exist.");
            }

            // Get all text files in the folder
            string[] textFiles = Directory.GetFiles(folderPath, "*.txt");

            // Read the content of each file and return as a string array
            string[] fileContents = textFiles.Select(File.ReadAllText).ToArray();

            return fileContents;
        }
    }
}
