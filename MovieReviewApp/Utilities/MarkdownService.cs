using Markdig;
using Microsoft.AspNetCore.Components;

namespace MovieReviewApp.Utilities
{
    public class MarkdownService
    {
        private readonly MarkdownPipeline _pipeline;

        public MarkdownService()
        {
            // Configure the markdown pipeline with useful extensions
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseAutoLinks()
                .UseSoftlineBreakAsHardlineBreak()
                .Build();
        }

        /// <summary>
        /// Converts markdown text to HTML
        /// </summary>
        /// <param name="markdown">The markdown text to convert</param>
        /// <returns>HTML string</returns>
        public string ConvertToHtml(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return string.Empty;

            return Markdig.Markdown.ToHtml(markdown, _pipeline);
        }

        /// <summary>
        /// Converts markdown text to HTML wrapped in a MarkupString for direct rendering in Blazor
        /// </summary>
        /// <param name="markdown">The markdown text to convert</param>
        /// <returns>MarkupString containing the HTML</returns>
        public MarkupString ConvertToMarkupString(string? markdown)
        {
            var html = ConvertToHtml(markdown);
            return new MarkupString(html);
        }

        /// <summary>
        /// Adds <br/> tags to line breaks for storage
        /// </summary>
        public string AddLineBreaks(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Replace line breaks with <br/>
            var result = text.Replace("\r\n", "<br/>").Replace("\n", "<br/>").Replace("\r", "<br/>");
            
            // Add double <br/> at the end if not already present
            if (!result.EndsWith("<br/><br/>"))
            {
                if (result.EndsWith("<br/>"))
                    result += "<br/>";
                else
                    result += "<br/><br/>";
            }

            return result;
        }

        /// <summary>
        /// Removes <br/> tags for editing
        /// </summary>
        public string RemoveLineBreaks(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Remove all <br/> tags and replace with newlines
            return text.Replace("<br/>", "\n");
        }
    }
}