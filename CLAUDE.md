# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Essential Commands

### Development
```bash
# Build and run
dotnet restore
dotnet build
dotnet run --instance "dev"        # Run development instance
dotnet run --instance "dev" --port 5000  # Specify port

# Instance management
dotnet run --list                  # List all instances
dotnet run --help                  # Show help options

# Publishing
dotnet publish -c Release -o ./publish
```

### Linting and Code Quality
The project uses .NET's built-in code analysis. To check for issues:
```bash
dotnet build -warnaserror
```

## Architecture Overview

This is a **Blazor Server** application (.NET 9.0) designed for movie discussion groups with audio recording, transcription, and AI analysis capabilities.

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

1. **Dependency Injection**: All services registered in `Program.cs`
2. **Async/Await**: Used consistently throughout the codebase
3. **Explicit Typing**: No `var` keyword - all types explicitly declared
4. **SignalR Integration**: Real-time updates for audio processing status
5. **CSS Variables**: Theme system uses CSS custom properties

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
- Use existing repository pattern in `Infrastructure/Database`
- Ensure instance isolation through `IInstanceService`
- GridFS for files > 16MB