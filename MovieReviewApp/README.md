# 🎬 Movie Review App

A beautiful, modern web application for managing movie selections, discussions, and reviews with your friends and family. Features a cyberpunk-inspired dark theme with secure API key management and **multiple instance support** for complete isolation between different groups.

## ✨ Features

- **Multiple Isolated Instances** - Run unlimited instances for different groups (Family, Work, Friends, etc.)
- **Movie Theater History View** - Browse your movie history like a professional streaming site
- **Secure API Integration** - TMDB for movie data, Gladia for audio transcription
- **Award System** - Vote for your favorite movies of the year
- **Audio Processing** - Transcribe and process discussion recordings
- **Facebook Integration** - Optional Messenger integration for notifications
- **Responsive Design** - Beautiful cyberpunk UI that works on all devices
- **Secure Configuration** - First-run setup with encrypted secrets storage per instance

## 🏠 Instance System

Each instance is completely isolated with its own:
- ✅ **Database** - Separate MongoDB database per instance
- ✅ **Configuration** - Own API keys, settings, and preferences
- ✅ **Users & Movies** - No shared data between instances
- ✅ **Port & Access** - Each runs independently on different ports
- ✅ **Chat Integration** - Instance-specific Facebook groups

## 🚀 Quick Start

### Prerequisites

- .NET 9.0
- MongoDB instance (local or cloud)
- **FFmpeg** (required for audio processing) - see [Audio Processing Setup](#-audio-processing-setup) below
- API keys for:
  - [TMDB](https://www.themoviedb.org/settings/api) (required, free)
  - [Gladia](https://gladia.io/) (optional, for audio transcription)

### Installation

1. **Clone the repository**
   ```bash
   git clone [your-repo-url]
   cd MovieReviewApp
   ```

2. **Run your first instance**
   ```bash
   # Start a new instance
   dotnet run --instance "Family-Movies" --port 5000
   
   # Or let it auto-assign a port
   dotnet run --instance "Work-Film-Club"
   ```

3. **First-Run Setup**
   - The app will automatically redirect you to `/setup` on first run
   - Configure your instance name and display name
   - Enter your MongoDB connection string
   - Enter your TMDB API key (required)
   - Enter your Gladia API key (optional, for audio transcription)
   - Configure Facebook integration (optional)
   - Choose content type (General or Family)
   - Complete the setup and start using the app!

## 🎯 Running Multiple Instances

```bash
# Family movie nights
dotnet run --instance "Family-Movies" --port 5000

# Work film club  
dotnet run --instance "Work-Film-Club" --port 5001

# Friends group
dotnet run --instance "Friends-Cinema" --port 5002

# Book-to-movie club
dotnet run --instance "Book-Adaptations" --port 5003
```

### Instance Management Commands

```bash
# List all existing instances
dotnet run --list

# Get help with commands
dotnet run --help

# Start specific instance on specific port
dotnet run --instance "My-Instance" --port 5005
```

## 🔒 Security Features

- **Per-Instance Secure Storage** - Each instance has its own encrypted secrets
- **First-Run Setup Wizard** - Easy configuration without touching config files
- **Template Configuration** - Public distribution includes template files with placeholder values
- **No Sensitive Data in Git** - Comprehensive .gitignore prevents accidental commits

## 📁 Instance Storage

Each instance stores its data separately:
- **Windows**: `%APPDATA%/MovieReviewApp/instances/{instance-name}/`
- **macOS**: `~/.config/MovieReviewApp/instances/{instance-name}/`
- **Linux**: `~/.config/MovieReviewApp/instances/{instance-name}/`

**Per instance files:**
- `secrets.json` - API keys and sensitive configuration
- `config.json` - Instance settings (display name, port, content type)

## 🎨 UI Features

### Theater View
- **3-column movie grid** on desktop (responsive to 2 on tablet, 1 on mobile)
- **Movie posters** with hover effects and overlay information
- **Newest movies first** sorting
- **Click to view details** with comprehensive modal

### List View
- **Traditional table format** for detailed information
- **All movie data** visible at once
- **Sortable columns** for different viewing preferences

## 🎵 Audio Processing Setup

### FFmpeg Installation (Required)

The application automatically converts large WAV files (>100MB) to MP3 for faster, more reliable uploads to Gladia. This requires FFmpeg to be installed on your system.

#### Windows
```bash
# Using winget (recommended)
winget install FFmpeg

# Or download from https://ffmpeg.org/download.html
```

#### macOS
```bash
# Using Homebrew
brew install ffmpeg
```

#### Linux (Ubuntu/Debian)
```bash
# Using apt
sudo apt update
sudo apt install ffmpeg
```

#### Linux (CentOS/RHEL/Fedora)
```bash
# Using yum/dnf
sudo yum install ffmpeg
# or
sudo dnf install ffmpeg
```

### Audio Processing Features

- **Smart Compression**: Files >100MB automatically converted to MP3 before upload
- **Quality Optimization**: 128kbps MP3 with 44.1kHz sample rate (perfect for speech transcription)
- **Size Reduction**: Typical 600MB WAV → 60MB MP3 (90% reduction)
- **Reliability**: Smaller files eliminate upload timeouts and stream errors
- **Automatic Cleanup**: Temporary files are automatically deleted after processing

### Supported Audio Formats

**Input formats**: WAV, MP3, M4A, AAC, OGG, FLAC, MP4, MOV, AVI, MKV, WEBM, M4V, 3GP  
**Upload optimization**: Large WAV files are automatically converted to MP3 for better performance

## 🛠️ Development

### Development Setup

1. **For development with template files:**
   ```bash
   cp appsettings.json.template appsettings.json
   ```

2. **Use .NET User Secrets for development (optional):**
   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "TMDB:ApiKey" "your-tmdb-key"
   dotnet user-secrets set "Gladia:ApiKey" "your-gladia-key"
   ```

3. **Or use the secure first-run setup (recommended):**
   ```bash
   dotnet run --instance "dev"
   # Follow the setup wizard at http://localhost:5000/setup
   ```

### Building

```bash
dotnet build
dotnet run
```

## 🚀 Deployment

For production deployment:

1. Set up your MongoDB instance
2. Configure environment variables for MongoDB connections
3. Run the application - it will guide you through secure setup
4. Optional: Set up reverse proxy (nginx/Apache) for HTTPS

### IIS Deployment

For detailed IIS deployment instructions, including how to set up multiple instances, see our comprehensive [IIS Deployment Guide](docs/IIS_DEPLOYMENT.md).

Key points for IIS deployment:
- Configure instance and port using command line arguments in web.config
- Each instance maintains separate configuration and database
- No port configuration needed in the UI - handled via deployment configuration

## 📝 License

This project is for personal use. Please respect API terms of service for TMDB and Gladia.

## 🤝 Contributing

This is a personal family project, but feel free to fork and adapt for your own use!

## ⚠️ Important Notes

- **Never commit API keys** to version control
- **Regenerate any exposed keys** immediately
- **Keep your secrets.json file secure** and backed up
- **Respect API rate limits** for all integrated services

## 🆘 Troubleshooting

### First Run Issues
- Ensure MongoDB is running and accessible
- Check that all required environment variables are set
- Verify API keys are valid and active

### Missing Configuration
- Delete the instance folder `%APPDATA%/MovieReviewApp/instances/{instance-name}/` to reset setup
- Or delete just the `secrets.json` file for that instance
- Restart the application to trigger first-run setup again

### API Issues
- Verify your API keys are active and have sufficient credits
- Check API rate limits if requests are failing
- Ensure network connectivity to external APIs

### Audio Processing Issues
- **FFmpeg not found**: Install FFmpeg using the instructions in [Audio Processing Setup](#-audio-processing-setup)
- **Large file upload failures**: The app automatically converts >100MB WAV files to MP3, but requires FFmpeg
- **"Error while copying content to a stream"**: Usually resolved by MP3 conversion (requires FFmpeg)
- **Conversion failures**: Check FFmpeg installation with `ffmpeg -version` in terminal/command prompt

## 🏗️ Project Architecture

The application follows **Clean Architecture** principles with clear separation of concerns:

### Architecture Overview

```
Application/
├── Services/              # Business logic services
│   ├── AudioClipService.cs      # Audio clip generation
│   ├── MovieReviewService.cs    # Core movie review logic
│   ├── MovieSessionService.cs   # Movie session management
│   └── ThemeService.cs          # Theme management
│
Infrastructure/
├── Configuration/         # Application configuration
├── Database/             # Data access layer
│   └── MongoDbService.cs       # MongoDB implementation
├── FileSystem/           # File operations
│   └── ImageService.cs         # Image processing
├── Repositories/         # Data repositories
│   ├── AwardEventRepository.cs
│   ├── PersonRepository.cs
│   └── SoundClipRepository.cs
└── Services/             # External service integrations
    ├── GladiaService.cs        # Audio transcription
    ├── MessengerService.cs     # Facebook integration
    └── SecureConfigurationProvider.cs
│
Core/
└── Interfaces/           # Domain interfaces
    └── IDatabaseService.cs     # Database abstraction
│
Utilities/                # Helper classes and extensions
├── DateExtensions.cs           # Date utility methods
└── EnumHelper.cs              # Enum utilities
```

### Key Architecture Principles

- **Clean Architecture**: Organized into distinct layers with clear dependencies
- **Repository Pattern**: Data access through repositories implementing interfaces
- **Dependency Injection**: All services use interface abstraction (IDatabaseService)
- **SOLID Principles**: Especially Dependency Inversion with interface-based design
- **Instance Isolation**: Complete separation between different group instances

### Coding Standards

#### Type Declaration Standards
- **NEVER use `var` keyword**: All variable declarations must use explicit types
- **Enforced by .editorconfig**: Build will fail if `var` is used anywhere
- **Examples**:
  ```csharp
  // CORRECT
  List<MovieSession> sessions = await _database.GetAllAsync<MovieSession>();
  string fileName = Path.GetFileName(filePath);
  Dictionary<string, int> counts = new Dictionary<string, int>();
  
  // INCORRECT - Build error
  var sessions = await _database.GetAllAsync<MovieSession>();
  ```

### Service Registration

Services are registered in Program.cs using dependency injection:

```csharp
// Database abstraction
builder.Services.AddSingleton<IDatabaseService, MongoDbService>();

// Application services
builder.Services.AddScoped<MovieReviewService>();
builder.Services.AddScoped<MovieSessionService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AudioClipService>();

// Infrastructure services
builder.Services.AddScoped<ImageService>();
builder.Services.AddScoped<GladiaService>();
builder.Services.AddScoped<MessengerService>();

// Repositories
builder.Services.AddScoped<AwardEventRepository>();
builder.Services.AddScoped<PersonRepository>();
builder.Services.AddScoped<SoundClipRepository>();
```

## 🔧 Development

### Prerequisites
- .NET 8.0
- MongoDB instance
- FFmpeg (for audio processing)

### Development Setup
1. **Clone and build**:
   ```bash
   git clone [repo-url]
   cd MovieReviewApp
   dotnet build
   ```

2. **Start development instance**:
   ```bash
   dotnet run --instance "dev"
   # Follow setup wizard at http://localhost:5000/setup
   ```

### Build Commands
```bash
dotnet build                    # Standard build with validation
dotnet build -c Release        # Release build for production
```

---

Made with ❤️ for movie lovers everywhere!