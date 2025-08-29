# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Essential Commands

### Development
```bash
# Build and run
dotnet restore
dotnet build
dotnet run --instance "dev"        # Run development instance (auto-assigns port)
dotnet run --instance "dev" --port 5000  # Run with specific port

# Working directory context (run from solution root)
cd MovieReviewApp  # Navigate to main project directory before running

# Instance management
dotnet run --list                  # List all instances
dotnet run --help                  # Show help options

# Publishing
dotnet publish -c Release -o ./publish
```

### Testing
The project uses xUnit for testing. Run tests with:
```bash
# Run all tests
dotnet test

# Run tests with verbosity
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~HomePageDataServiceTests"

# Run tests in specific project only
dotnet test MovieReviewApp.Tests/
```

### Linting and Code Quality
The project enforces strict code quality rules through .editorconfig:
```bash
# Build with warnings as errors (enforces code quality)
dotnet build -warnaserror

# Check for format issues
dotnet format --verify-no-changes
```

**Important Code Standards:**
- **No `var` keyword** - All types must be explicitly declared (enforced as error)
- **Explicit typing everywhere** - `string name = "value"` not `var name = "value"`
- **File-scoped namespaces** - Use `namespace MovieReviewApp;` not `namespace MovieReviewApp { }`

## Architecture Overview

This is a **Blazor Server** application (.NET 9.0) designed for movie discussion groups with audio recording, transcription, and AI analysis capabilities.

**Solution Structure:**
- `MovieReviewApp/` - Main Blazor Server application
- `MovieReviewApp.Tests/` - xUnit test project
- `Application/` - Business logic layer (outside main project)

### Core Architecture Patterns

1. **Multi-Instance Design**: Each movie group runs as an isolated instance with its own MongoDB database and configuration. Instance isolation is enforced at the service layer through `InstanceService`.

2. **Clean Architecture Layers**:
   - **Controllers**: HTTP endpoints for file uploads and API access
   - **Application/Services**: Business logic, organized by feature (Analysis, Processing, etc.)
   - **Infrastructure**: External concerns (Database, FileSystem, Configuration)
   - **Components**: Blazor UI components following a Pages/Partials structure

3. **Audio Processing Pipeline**: State machine pattern in `AudioProcessingService` handles the workflow:
   - Upload → Conversion (if needed) → Transcription → Analysis → Storage
   - Large files automatically converted from WAV to MP3
   - Integration with Gladia API for transcription

4. **AI Analysis System**: Modular design allows multiple AI providers:
   - `IMovieAnalysisService` interface implemented by OpenAI and Claude services
   - Prompt generation in `PromptGenerationService`
   - Response parsing and storage of analysis results

5. **Database Design**: MongoDB with GridFS for file storage
   - Each instance has its own database named `MovieReviewApp_{instanceName}`
   - Repository pattern abstracts data access
   - GridFS stores audio files and images

### Key Services and Their Responsibilities

- **InstanceService**: Manages multi-instance configuration and isolation
- **SecretsManager**: Encrypted storage of API keys per instance
- **AudioProcessingService**: Orchestrates the audio processing pipeline
- **MovieEventService**: Core business logic for movie sessions
- **ThemeService**: Manages 14 themes (7 base × 2 variants)
- **DemoProtectionService**: Enforces read-only mode for demo instances

### Configuration and Secrets

- API keys are stored encrypted per instance in `secrets/` directory
- Never commit actual API keys - use `appsettings.json.template` as reference
- Configuration is loaded through `SecureConfigurationProvider`

### Important Patterns

1. **Dependency Injection**: All services registered in `Program.cs` with proper lifetime management
2. **Async/Await**: Used consistently throughout the codebase
3. **Explicit Typing**: No `var` keyword allowed - enforced at build time as error
4. **Command-Line Arguments**: Instance and port management via `CommandLineParser`
5. **Multi-Instance Isolation**: Each instance gets separate database and configuration
6. **SignalR Integration**: Real-time updates for audio processing status
7. **State Machine Pattern**: Audio processing workflow in `AudioProcessingStateMachine`
8. **Repository Pattern**: Database access abstracted through `MongoDbService`
9. **Secure Configuration**: API keys encrypted per instance via `SecretsManager`
10. **CSS Variables**: Theme system uses CSS custom properties

### Common Development Tasks

When modifying audio processing:
- Check `AudioProcessingService` for the state machine logic
- Update `ProcessingStatus` enum if adding new states
- Ensure SignalR notifications are sent for UI updates

When adding new AI analysis features:
- Implement `IMovieAnalysisService` interface
- Add configuration in `appsettings.json`
- Update `PromptGenerationService` for new prompt types

When working with the database:
- Use `MongoDbService` for data access - it provides generic CRUD operations
- Database naming: `MovieReviewApp_{instanceName}` (automatic isolation)
- Collections use type-based naming with `[CollectionName]` attributes
- GridFS for audio files and large assets
- Instance isolation is automatic through `InstanceManager`

When working with audio processing:
- Check `AudioProcessingStateMachine` for workflow state management
- All processing is asynchronous with progress tracking
- Files automatically converted from WAV to MP3 if >100MB
- FFmpeg dependency required for audio conversion

When adding new themes:
- Update `ThemeService` with new theme definitions
- CSS variables defined in theme-specific stylesheets
- Each theme supports both dark and light variants