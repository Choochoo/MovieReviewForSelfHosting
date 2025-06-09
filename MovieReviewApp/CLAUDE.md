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
- **Database/MongoDbService.cs** - Unified MongoDB service with type-based and enum-based collection access
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

## Audio Processing & FFmpeg

### FFmpeg Requirement
The application requires FFmpeg for optimal audio processing:
- **Smart MP3 Conversion**: Large WAV files (>100MB) automatically converted to MP3 before Gladia upload
- **Performance**: 600MB WAV → 60MB MP3 (90% reduction) for faster, more reliable uploads
- **Quality Settings**: 128kbps MP3 with 44.1kHz sample rate (optimal for speech transcription)
- **Temporary Files**: Automatic cleanup of converted files after processing

### Audio Processing Pipeline
1. **File Detection**: `MovieSessionService.cs` scans for audio files with pattern recognition
2. **Smart Conversion**: `GladiaService.cs` automatically converts large WAV files using FFmpeg
3. **Upload**: Optimized files uploaded to Gladia API for transcription
4. **Analysis**: `MovieSessionAnalysisService.cs` processes transcripts for entertainment moments
5. **Clip Generation**: `AudioClipService.cs` creates highlight clips from analysis results

### Supported Audio Formats
**Input**: WAV, MP3, M4A, AAC, OGG, FLAC, MP4, MOV, AVI, MKV, WEBM, M4V, 3GP  
**Processing**: Large files automatically optimized for upload performance

## Testing and Quality

### Build Commands
```bash
dotnet build                    # Standard build with validation
dotnet build -c Release        # Release build for production
```

### Running Tests
No formal test suite currently implemented. Manual testing through:
- Instance creation and configuration
- Audio file processing workflows
- Database operations across collections
- API integrations (TMDB, Gladia, Facebook)

## Important Development Guidelines

### Coding Standards

#### Explicit Type Declarations
- **NEVER use `var` keyword**: All variable declarations must use explicit types
- **Enforced by .editorconfig**: The `.editorconfig` file enforces this as an error-level rule
- **Examples**:
  ```csharp
  // CORRECT
  List<MovieSession> sessions = await _database.GetAllAsync<MovieSession>();
  string fileName = Path.GetFileName(filePath);
  Dictionary<string, int> counts = new Dictionary<string, int>();
  
  // INCORRECT - Will cause build errors
  var sessions = await _database.GetAllAsync<MovieSession>();
  var fileName = Path.GetFileName(filePath);
  var counts = new Dictionary<string, int>();
  ```

#### Architecture Principles
- **Clean Architecture**: Organized into Application, Infrastructure, Core, and Utilities layers
- **Repository Pattern**: Data access through repositories implementing interfaces
- **Dependency Injection**: All services use interface abstraction (e.g., IDatabaseService)
- **SOLID Principles**: Especially Dependency Inversion with interface-based design
- **DRY & KISS**: Enforce "Don't Repeat Yourself" and "Keep It Simple Stupid" concepts
- **Base Services**: Common functionality shared through base classes and interfaces

### Project Structure (Clean Architecture)

```
Application/
├── Services/           # Business logic services
Infrastructure/
├── Configuration/      # App configuration
├── Database/          # Data access layer
├── FileSystem/        # File operations
├── Repositories/      # Data repositories
└── Services/          # External service integrations
Core/
└── Interfaces/        # Domain interfaces
Utilities/             # Helper classes and extensions
```

### Git Commit Standards
- **Never mention Claude**: Do not include any references to Claude, AI assistance, or automated generation in commit messages
- **Focus on functionality**: Describe what was changed and why, not how it was created
- **Be descriptive**: Use clear, concise commit messages that explain the business value

### Security Requirements
- **No API Keys in Code**: All sensitive data stored in per-instance encrypted `secrets.json`
- **Template Distribution**: Public repository contains only `appsettings.json.template` with placeholders
- **Instance Isolation**: Each instance has completely separate configuration and data

### Configuration Management
- **First-Run Setup**: New instances automatically redirect to `/setup` for secure configuration
- **Template-Based**: Development uses `appsettings.json.template` copied to `appsettings.json`
- **Per-Instance Storage**: Configuration stored in platform-specific app data directories