namespace MovieReviewApp.Services;

public class PromptService
{
    private readonly OpenAIService _openAIService;
    private readonly ClaudeService _claudeService;
    private readonly ILogger<PromptService> _logger;

    public PromptService(OpenAIService openAIService, ClaudeService claudeService, ILogger<PromptService> logger)
    {
        _openAIService = openAIService;
        _claudeService = claudeService;
        _logger = logger;
    }

    public bool IsConfigured => _openAIService.IsConfigured || _claudeService.IsConfigured;

    public string GetAvailableProvider()
    {
        if (_openAIService.IsConfigured && _claudeService.IsConfigured)
            return "OpenAI (Primary) + Claude (Backup)";
        else if (_openAIService.IsConfigured)
            return "OpenAI";
        else if (_claudeService.IsConfigured)
            return "Claude";
        else
            return "None configured";
    }

    public async Task<string?> ProcessPromptAsync(string promptTemplate, params object[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
        {
            _logger.LogWarning("No parameters provided for prompt template");
            return null;
        }

        try
        {
            var formattedPrompt = string.Format(promptTemplate, parameters);
            return await ExecutePromptAsync(formattedPrompt);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Error formatting prompt template");
            return null;
        }
    }

    private async Task<string?> ExecutePromptAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        // Try OpenAI first (primary)
        if (_openAIService.IsConfigured)
        {
            try
            {
                _logger.LogInformation("Executing prompt using OpenAI (primary)");
                var result = await _openAIService.ExecutePromptAsync(prompt);
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.LogInformation("Successfully executed prompt using OpenAI");
                    return result;
                }
                _logger.LogWarning("OpenAI returned empty result, trying Claude as backup");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI failed, trying Claude as backup");
            }
        }

        // Try Claude as backup (secondary)
        if (_claudeService.IsConfigured)
        {
            try
            {
                _logger.LogInformation("Executing prompt using Claude (secondary)");
                var result = await _claudeService.ExecutePromptAsync(prompt);
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.LogInformation("Successfully executed prompt using Claude");
                    return result;
                }
                _logger.LogWarning("Claude also returned empty result");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Claude also failed to execute prompt");
            }
        }

        _logger.LogError("Both OpenAI and Claude failed or are not configured");
        return null;
    }

    // Pre-defined prompt templates
    public static class Templates
    {
        public const string TextSpicing = @"Take this text and make it {0} for an HTML form input that accepts HTML markup. The output should be on a single line with inline styles and animations. It needs to scale properly between mobile and desktop using clamp() for responsive font sizing:

{1}

Style choice: {0}

Requirements:
- Single line HTML with style tag for animations
- Must fit in 100% width container
- Responsive font sizing with clamp()
- No italic text for mobile readability
- Maximum visual impact

Return only the styled HTML, no explanations or additional text.";
    }

    // Convenience method for text spicing
    public async Task<string?> SpiceUpTextAsync(string text, string style)
    {
        return await ProcessPromptAsync(Templates.TextSpicing, style, text);
    }
}