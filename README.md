# 🎬 Movie Review App

> **A sophisticated Blazor Server application for movie discussion groups with AI-powered audio analysis and multi-instance architecture**

> [!TIP]
> [**Try Demo**](http://ourfilmclub.duckdns.org:5015)

A professional-grade web application built with .NET 9.0 and MongoDB, featuring advanced audio processing, AI-powered conversation analysis, and complete multi-tenancy support. I use this with my two movie groups: one with my family and one with my friends. Perfect for movie clubs, family groups, and professional film discussions.

![Home Dashboard](screenshots/home-dashboard.png)
*Modern cyberpunk-inspired interface with timeline view*

## 🏆 Key Highlights

- **🎯 Multi-Instance Architecture** - Complete isolation between groups with separate databases and configurations
- **🤖 AI-Powered Analysis** - OpenAI and Claude integration for conversation insights and entertainment highlights
- **🎵 Advanced Audio Processing** - Multi-microphone transcription with speaker diarization via Gladia API
- **🏛️ Clean Architecture** - SOLID principles with dependency injection and repository patterns
- **🔒 Enterprise Security** - Encrypted per-instance configuration and API key management
- **📱 Responsive Design** - Beautiful dark theme optimized for all devices

## ✨ Core Features

### 🎭 Movie Management
- **Smart Movie Selection** - TMDB integration with poster fetching and synopsis caching
- **Phase-Based Scheduling** - Automatic rotation through participants with configurable cycles
- **Award Voting System** - Comprehensive voting for best movies, performances, and moments
- **Discussion Questions** - Curated prompts to enhance movie conversations

![Movie Timeline](screenshots/movie-timeline.png)
*Chronological timeline with phases and award events*

![Movie Timeline Details](screenshots/movie-timeline-details.png)
*Detailed view of movie session with comprehensive information*

### 🎵 Audio Processing Pipeline
- **Multi-Microphone Support** - Process individual participant audio files
- **Automatic Optimization** - Smart WAV to MP3 conversion for files >100MB using FFmpeg
- **Speaker Diarization** - Gladia API integration for speaker identification
- **State Machine Processing** - Robust workflow with retry logic and error handling

![Audio Processing](screenshots/audio-processing.png)
*Real-time audio processing with progress tracking*

### 🤖 AI-Powered Analysis
- **Conversation Highlights** - OpenAI analysis for funniest moments and best quotes
- **Speaker Statistics** - Word counts, interruption tracking, and participation metrics
- **Best Moments Extraction** - Automated identification of entertainment highlights
- **Detailed Insights** - Claude integration for comprehensive discussion analysis

![AI Analysis](../screenshots/ai-analysis.png)
*AI-generated conversation insights and entertainment highlights*

### 🎨 Customizable Themes & Appearance
- **7 Theme Families** - Choose from Cyberpunk, Ocean, Nature, Western, Vintage, Noir, or SciFi styles
- **Dark/Light Mode** - Toggle between dark and light variants for each theme
- **14 Total Variants** - Every theme family has both dark and light modes
- **Per-Instance Themes** - Each group can have their own unique visual identity
- **Easy Switching** - Change themes instantly from the Settings page

![Theme Customization](../screenshots/theme-options.png)
*Choose from 7 beautiful theme families with dark/light mode options*

#### Available Theme Families

| Theme | Description | Best For |
|-------|-------------|----------|
| **🌃 Cyberpunk** | Neon blues and purples with futuristic styling | Tech-savvy groups, sci-fi fans |
| **🌊 Ocean** | Calming blues and teals inspired by the sea | Relaxed discussions, family groups |
| **🌿 Nature** | Earth tones with green accents | Outdoor enthusiasts, nature lovers |
| **🤠 Western** | Warm browns and golds with rustic styling | Adventure fans, period film enthusiasts |
| **📜 Vintage** | Classic sepia tones with retro aesthetics | Classic film buffs, nostalgia lovers |
| **🕵️ Noir** | High contrast blacks and whites with dramatic styling | Mystery fans, film noir enthusiasts |
| **🚀 SciFi** | Sleek metallics and electric accents | Science fiction groups, futuristic themes |

#### Dark & Light Modes
Each theme family includes both **dark** and **light** variants:
- **Dark Mode**: Rich, deep colors perfect for evening viewing sessions
- **Light Mode**: Clean, bright interface ideal for daytime discussions

> **💡 Pro Tip**: Each instance remembers its theme preference, so your family group can use Nature Dark while your work film club uses Vintage Light!

## 🏗️ Multi-Instance Architecture

![Instance Setup](../screenshots/instance-setup.png)
*First-run setup wizard for new instances*

### Complete Isolation
Each instance maintains complete separation:

| Component | Isolation Level | Description |
|-----------|----------------|-------------|
| **Database** | ✅ Separate MongoDB | Independent collections per instance |
| **Configuration** | ✅ Encrypted Storage | Per-instance API keys and settings |
| **File Storage** | ✅ Organized Folders | Instance-specific audio and image storage |
| **Network** | ✅ Port-Based | Each instance runs on its own port |
| **Security** | ✅ Encrypted Secrets | No shared sensitive data |

### Use Cases
```bash
# Family movie nights
dotnet run --instance "Family-Movies" --port 5000

# Work film club
dotnet run --instance "Work-Film-Club" --port 5001

# Friends cinema group
dotnet run --instance "Friends-Cinema" --port 5002

# Book-to-movie adaptations club
dotnet run --instance "Book-Adaptations" --port 5003
```

## 🚀 Quick Start

### Prerequisites

- **.NET 9.0** - Latest long-term support version
- **MongoDB** - Local instance or cloud (MongoDB Atlas recommended)
- **FFmpeg** - Required for audio optimization and conversion
- **API Keys**:
  - [**TMDB**](https://www.themoviedb.org/settings/api) - Movie data and posters (required, free)
  - [**Gladia**](https://gladia.io/) - Audio transcription with speaker diarization (optional)
  - [**OpenAI**](https://platform.openai.com/) - Conversation analysis (optional)
  - [**Claude**](https://console.anthropic.com/) - Detailed insights (optional)

> **💡 Demo Mode Available**: Try the app without any API keys using the demo instance

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

4. **Customize Your Experience**
   - Visit **Settings** to choose your preferred theme (Cyberpunk, Nature, Ocean, Western, Vintage, Noir, or SciFi)
   - Toggle between Dark/Light mode with the theme switcher
   - Each instance remembers its own theme preferences

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

## 🏗️ Technical Architecture

*Clean Architecture implementation with dependency injection*

### Core Design Patterns

- **🏛️ Clean Architecture** - Clear separation of concerns across layers
- **📦 Repository Pattern** - Data access abstraction with MongoDB
- **💉 Dependency Injection** - Interface-based service registration
- **🔄 State Machine Pattern** - Audio processing workflow management
- **🛡️ Factory Pattern** - Secure configuration provider creation

### Technology Stack

| Layer | Technology | Purpose |
|-------|------------|----------|
| **Frontend** | Blazor Server + SignalR | Real-time UI with C# |
| **Backend** | .NET 9.0 + ASP.NET Core | High-performance web framework |
| **Database** | MongoDB 7.0+ | Document storage with GridFS |
| **Audio** | NAudio + FFmpeg | Audio processing and conversion |
| **AI Services** | OpenAI + Claude APIs | Conversation analysis |
| **Transcription** | Gladia API | Speaker diarization |

### Project Structure

```
📁 Application/
├── 🧠 Services/Analysis/       # AI-powered conversation analysis
├── 🎵 Services/Processing/     # Audio processing state machine
├── 📊 Models/                  # Domain models and DTOs
└── 🔧 Services/               # Core business logic

📁 Infrastructure/
├── ⚙️ Configuration/          # Multi-instance management
├── 🗄️ Database/              # MongoDB abstraction layer
├── 📁 FileSystem/             # File operations and storage
└── 🌐 Services/              # External API integrations

📁 Components/
├── 📄 Pages/                  # Main application pages
├── 🧩 Partials/              # Reusable UI components
└── 🎨 Layout/                # Application shell

📁 Models/                     # Core domain entities
📁 Utilities/                  # Helper extensions and tools
```

### 🛠️ Development Standards

#### Code Quality
- **✅ Explicit Type Declarations** - No `var` keyword usage (enforced by .editorconfig)
- **📝 XML Documentation** - Comprehensive method documentation
- **🧪 SOLID Principles** - Dependency inversion with interface abstractions
- **🔒 Security-First** - No hardcoded secrets, encrypted configuration storage
- **⚡ Performance** - Async/await patterns, efficient MongoDB queries

#### Key Design Decisions

```csharp
// ✅ Explicit types and dependency injection
public class MovieSessionService
{
    private readonly IDatabaseService _database;
    
    /// <summary>
    /// Analyzes movie session audio with AI-powered insights.
    /// </summary>
    public async Task<AnalysisResult> AnalyzeSessionAsync(Guid sessionId)
    {
        List<AudioSegment> segments = await _database.GetAllAsync<AudioSegment>();
        return await _aiAnalysisService.ProcessSegmentsAsync(segments);
    }
}
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

## 🚀 Getting Started

### Demo Mode
```bash
# Try the app immediately with demo data
dotnet run --instance "demo"
# Visit http://localhost:5088
```

### Development Setup

1. **Clone and build**:
   ```bash
   git clone https://github.com/Choochoo/MovieReviewApp.git
   cd MovieReviewApp/MovieReviewApp
   dotnet restore
   dotnet build
   ```

2. **Start development instance**:
   ```bash
   dotnet run --instance "dev"
   # Visit http://localhost:5000/setup
   ```

3. **Production build**:
   ```bash
   dotnet build -c Release
   dotnet publish -c Release
   ```

### Configuration Management

```bash
# List all instances
dotnet run --list

# Create new instance
dotnet run --instance "my-group" --port 5010

# Reset instance configuration
# Delete: %APPDATA%/MovieReviewApp/instances/my-group/
```

---

## 📊 Performance & Metrics

- **⚡ Load Time** - Sub-second page loads with SignalR real-time updates
- **🗄️ Storage** - Efficient MongoDB document storage with GridFS for large files
- **📈 Scalability** - Multi-instance architecture supports unlimited concurrent groups
- **🔒 Security** - Encrypted configuration storage with per-instance isolation
- **📱 Responsive** - Optimized for desktop, tablet, and mobile devices

## 🎯 Use Cases

- **🏠 Family Movie Nights** - Track movies, discussions, and favorite moments
- **🏢 Corporate Film Clubs** - Professional movie discussion groups
- **🎓 Film Studies** - Academic analysis with AI-powered insights
- **👥 Friend Groups** - Social movie watching with conversation highlights
- **📚 Book Clubs** - Book-to-movie adaptation discussions

---

<div align="center">

**Made with ❤️ for movie lovers everywhere!**

*Built with .NET 9.0, MongoDB, and modern web technologies*

[🌟 Star this repo](https://github.com/Choochoo/MovieReviewApp) • [🐛 Report issues](https://github.com/Choochoo/MovieReviewApp/issues) • [📖 Documentation](https://github.com/yourusername/MovieReviewApp/wiki)

</div>
