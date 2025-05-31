# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Run Commands

### Build
```bash
dotnet build                           # Standard build
dotnet build -c Release               # Release build
dotnet publish -c Release -r linux-arm64  # Publish for Linux ARM64
```

### Run
```bash
# Run in Adult mode
dotnet run --environment Development --audience Adult

# Run in Kid mode  
dotnet run --environment Development --audience Kid

# Using launch profiles
dotnet run --launch-profile "http Adult"
dotnet run --launch-profile "http Kid"
```

The application runs on `http://localhost:5212` by default.

### Environment Variables
- `MOVIEREVIEW_MONGO` - MongoDB connection string for Adult mode
- `MOVIEREVIEW_KID_MONGO` - MongoDB connection string for Kid mode  
- `AUDIENCE` - Set to "Adult" or "Kid" to determine mode

## Architecture Overview

This is a Blazor Server application (.NET 8.0) for tracking movie reviews across phases, with support for awards/voting and audio discussion transcription.

### Key Architectural Patterns

1. **Multi-Audience System**: The app supports two distinct modes (Adult/Kid) with:
   - Separate MongoDB databases per audience
   - Different color schemes and UI themes
   - Different Facebook chat URLs
   - Configuration loaded from `appsettings.{Audience}.json`

2. **Phase-Based Movie Tracking**: Movies are organized into phases (groups) with:
   - Automatic award events every N phases (configurable)
   - Meeting date scheduling per phase
   - Timeline visualization

3. **Audio Processing Pipeline**:
   - Upload audio/video files to `wwwroot/uploads/{Month}-{Year}/`
   - Transcription via AssemblyAI API with speaker diarization
   - Automatic file naming with duration
   - Error handling with quarantine folder

4. **Award System**:
   - Questions configured in database
   - 3 votes per person per question
   - Vote changes allowed within time limit
   - Results display after voting closes

### Component Organization

- **Components/Layout/** - Application layout components
- **Components/Pages/** - Routable page components with code-behind files
- **Components/Partials/** - Reusable UI components (awards, processing, stats)
- **Services/** - Business logic (MessengerService, StatsCommandProcessorService)
- **Database/MongoDb.cs** - MongoDB repository implementation
- **Handlers/** - Command processing handlers

### Database Collections

- `Phases` - Movie review phases
- `MovieReviews` - Individual movie events  
- `People` - Participants
- `Settings` - Application settings
- `StatsCommands` - Processed statistics
- `SiteUpdates` - Change tracking
- `AwardQuestions` - Award categories
- `AwardEvents` - Award periods
- `AwardVotes` - Individual votes

### Important Implementation Details

1. **Blazor Server Mode**: Uses `InteractiveServer` render mode for real-time updates
2. **File Uploads**: Handled via `ProcessAudio.razor` with size limits
3. **Statistics**: Command-based processing system via `StatsCommandProcessorService`
4. **Date Handling**: Custom extensions in `DateExtensions.cs` for phase calculations
5. **Configuration**: Multi-level configuration with base + audience-specific settings