using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MovieReviewApp.Models;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Core.Interfaces;
using MovieReviewApp.Database;

namespace MovieReviewApp.Components.Pages;

/// <summary>
/// Code-behind for the FirstRunSetup page component.
/// Handles the initial setup and configuration of the Movie Review App instance.
/// </summary>
public partial class FirstRunSetup : ComponentBase
{
    #region Private Fields
    
    private string tmdbKey = "";
    private string mongoConnection = "";
    private string gladiaKey = "";
    private string openaiKey = "";
    private string facebookChatUrl = "";
    private string groupName = "";
    private string contentType = "General";
    
    private bool isSaving = false;
    private bool isComplete = false;
    private string errorMessage = "";
    private InstanceConfig currentConfig = new();
    
    #endregion

    #region Injected Dependencies
    
    /// <summary>
    /// Service for managing application secrets and configuration.
    /// </summary>
    [Inject] 
    public SecretsManager SecretsManager { get; set; } = default!;

    /// <summary>
    /// Service for managing multiple application instances.
    /// </summary>
    [Inject] 
    public InstanceManager InstanceManager { get; set; } = default!;

    /// <summary>
    /// Navigation manager for routing and URL management.
    /// </summary>
    [Inject] 
    public NavigationManager Navigation { get; set; } = default!;

    /// <summary>
    /// JavaScript runtime for interacting with browser APIs.
    /// </summary>
    [Inject] 
    public IJSRuntime JS { get; set; } = default!;

    /// <summary>
    /// Database service for data persistence operations.
    /// </summary>
    [Inject] 
    public IDatabaseService DatabaseService { get; set; } = default!;
    
    #endregion

    #region Lifecycle Methods
    
    /// <summary>
    /// Initializes the component and checks if setup is already complete.
    /// If setup is complete, redirects to the main application.
    /// Otherwise, loads current instance configuration for defaults.
    /// </summary>
    protected override void OnInitialized()
    {
        // If setup is already complete, redirect to app
        if (!SecretsManager.IsFirstRun)
        {
            Navigation.NavigateTo("/");
            return;
        }

        // Load current instance config for defaults
        currentConfig = InstanceManager.GetInstanceConfig();
        groupName = currentConfig.DisplayName;
        contentType = currentConfig.Environment; // Map old Environment to new ContentType
    }
    
    #endregion

    #region Private Methods
    
    /// <summary>
    /// Saves the setup configuration including API keys, database settings, and instance configuration.
    /// Validates required fields and saves secrets and instance configuration to persistent storage.
    /// </summary>
    /// <returns>A task representing the asynchronous save operation.</returns>
    private async Task SaveSetup()
    {
        Console.WriteLine("SaveSetup started");
        isSaving = true;
        errorMessage = "";
        StateHasChanged();

        try
        {
            Console.WriteLine($"Validating: TMDB='{tmdbKey}', Mongo='{mongoConnection}'");
            
            // Validate required fields
            if (string.IsNullOrWhiteSpace(tmdbKey) || 
                string.IsNullOrWhiteSpace(mongoConnection) ||
                string.IsNullOrWhiteSpace(groupName))
            {
                errorMessage = "TMDB API key, MongoDB connection string, and Group Name are required.";
                Console.WriteLine($"Validation failed: {errorMessage}");
                return;
            }

            // Validate MongoDB connection string
            if (!mongoConnection.StartsWith("mongodb://") && !mongoConnection.StartsWith("mongodb+srv://"))
            {
                errorMessage = "MongoDB connection string must start with 'mongodb://' or 'mongodb+srv://'";
                return;
            }

            // Save all secrets
            var secrets = new Dictionary<string, string>
            {
                ["TMDB:ApiKey"] = tmdbKey.Trim(),
                ["MongoDB:ConnectionString"] = mongoConnection.Trim(),
                ["Facebook:ChatUrl"] = facebookChatUrl.Trim()
            };

            // Add Gladia key only if provided
            if (!string.IsNullOrWhiteSpace(gladiaKey))
            {
                secrets["Gladia:ApiKey"] = gladiaKey.Trim();
            }

            // Add OpenAI key only if provided
            if (!string.IsNullOrWhiteSpace(openaiKey))
            {
                secrets["OpenAI:ApiKey"] = openaiKey.Trim();
            }

            SecretsManager.SetSecrets(secrets);

            // Save instance configuration
            var instanceConfig = new InstanceConfig
            {
                InstanceName = InstanceManager.InstanceName,
                DisplayName = groupName.Trim(),
                Environment = contentType,
                Port = currentConfig.Port, // Keep existing port, will be set by command line
                Description = "", // No longer used
                CreatedDate = currentConfig.CreatedDate == default ? DateTime.UtcNow : currentConfig.CreatedDate,
                LastUsed = DateTime.UtcNow
            };

            InstanceManager.SaveInstanceConfig(instanceConfig);
            
            // Save group name to MongoDB settings
            await SaveGroupNameToSettings(groupName.Trim());
            
            Console.WriteLine("Setup completed successfully");
            isComplete = true;
            StateHasChanged();

            // Small delay for better UX
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Setup failed with exception: {ex}");
            errorMessage = $"Setup failed: {ex.Message}";
        }
        finally
        {
            Console.WriteLine("SaveSetup finished");
            isSaving = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Navigates to the main application page after setup completion.
    /// </summary>
    private void GoToApp()
    {
        Navigation.NavigateTo("/");
    }
    
    /// <summary>
    /// Saves the group name setting to the database.
    /// Creates a new setting if it doesn't exist, or updates the existing one.
    /// </summary>
    /// <param name="groupName">The group name to save to the database.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    private async Task SaveGroupNameToSettings(string groupName)
    {
        try
        {
            var setting = await DatabaseService.FindOneAsync<Setting>(s => s.Key == "GroupName");
            if (setting != null)
            {
                setting.Value = groupName;
                setting.UpdatedAt = DateTime.UtcNow;
                await DatabaseService.UpsertAsync(setting);
            }
            else
            {
                await DatabaseService.InsertAsync(new Setting 
                { 
                    Key = "GroupName", 
                    Value = groupName,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving group name to settings: {ex.Message}");
            // Non-critical error, continue with setup
        }
    }
    
    #endregion
}