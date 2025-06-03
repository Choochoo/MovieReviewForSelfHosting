# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Run Commands

### Build
```bash
dotnet build                           # Standard build
dotnet build -c Release               # Release build
dotnet publish -c Release -r linux-arm64  # Publish for Linux ARM64
```

### Run (Instance System)
```bash
# Interactive instance selection
dotnet run

# Run specific instance
dotnet run --instance "Family-Movies" --port 5000
dotnet run --instance "Work-Film-Club" --port 5001

# List all instances
dotnet run --list

# Get help
dotnet run --help
```

### Development Setup
For development, copy template configuration:
```bash
cp appsettings.json.template appsettings.json
```

Or use first-run setup (recommended):
```bash
dotnet run --instance "dev"
# Follow setup wizard at http://localhost:5000/setup
```

## Architecture Overview

This is a Blazor Server application (.NET 8.0) for tracking movie reviews across phases, with support for awards/voting and audio discussion transcription.

### Key Architectural Patterns

1. **Instance Isolation System**: The app supports unlimited isolated instances with:
   - Separate MongoDB databases per instance
   - Per-instance secure configuration storage
   - Instance-specific API keys and settings
   - Command-line instance management (`InstanceManager.cs`, `CommandLineParser.cs`)
   - First-run setup wizard with encrypted secrets (`SecretsManager.cs`)

2. **Secure Configuration Architecture**:
   - Template-based public distribution (`appsettings.json.template`)
   - Encrypted per-instance secrets storage (`SecretsManager.cs`)
   - Custom configuration provider (`SecureConfigurationProvider.cs`)
   - First-run middleware (`FirstRunSetupMiddleware.cs`)

3. **Phase-Based Movie Tracking**: Movies are organized into phases (groups) with:
   - Automatic award events every N phases (configurable)
   - Meeting date scheduling per phase
   - Timeline visualization

4. **Audio Processing Pipeline** (New Architecture):
   - Upload via `ProcessAudio.razor` and `UploadAudio.razor`
   - Transcription via Gladia API (`GladiaService.cs`)
   - Movie session analysis (`MovieSessionAnalysisService.cs`, `MovieSessionService.cs`)
   - File storage in `wwwroot/uploads/{Month}-{Year}/`
   - Processing status tracking

5. **Award System**:
   - Questions configured in database
   - 3 votes per person per question
   - Vote changes allowed within time limit
   - Results display after voting closes

### Component Organization

- **Components/Layout/** - Application layout components
- **Components/Pages/** - Routable page components with code-behind files  
- **Components/Partials/** - Reusable UI components (awards, processing, stats, sessions)
- **Services/** - Business logic and external integrations
  - Instance management (`InstanceManager.cs`, `SecretsManager.cs`)
  - Movie services (`MovieReviewService.cs`, `MovieSessionService.cs`)
  - External APIs (`GladiaService.cs`, `MessengerService.cs`)
  - Configuration (`SecureConfigurationProvider.cs`)
- **Database/GenericMongoDb.cs** - Generic MongoDB repository
- **Handlers/** - Command processing handlers
- **Middleware/** - Request pipeline components

### Database Collections

- `Phases` - Movie review phases
- `MovieEvents` - Individual movie events  
- `People` - Participants
- `Settings` - Application settings
- `StatsCommands` - Processed statistics
- `SiteUpdates` - Change tracking
- `AwardQuestions` - Award categories
- `AwardEvents` - Award periods
- `AwardVotes` - Individual votes
- `MovieSessions` - Audio/discussion sessions (new)

### Instance Storage Locations

Per-instance files stored in:
- **Windows**: `%APPDATA%/MovieReviewApp/instances/{instance-name}/`
- **macOS/Linux**: `~/.config/MovieReviewApp/instances/{instance-name}/`

Files per instance:
- `secrets.json` - Encrypted API keys and sensitive data
- `config.json` - Instance settings (display name, port, content type)

### Important Implementation Details

1. **Instance Isolation**: Each instance runs independently with isolated data and configuration
2. **Blazor Server Mode**: Uses `InteractiveServer` render mode for real-time updates
3. **Security**: Template-based distribution prevents API key exposure in git
4. **File Uploads**: 10GB limit configured for large audio/video files
5. **Audio Processing**: Gladia API integration replaces AssemblyAI
6. **Configuration**: Secure per-instance configuration with first-run setup
7. **Command Line**: Rich CLI interface for instance management
8. **Date Handling**: Custom extensions in `DateExtensions.cs` for phase calculations

### API Integrations

- **TMDB**: Movie data and posters (required)
- **Gladia**: Audio transcription with speaker diarization (optional)
- **Facebook**: Messenger integration for notifications (optional)