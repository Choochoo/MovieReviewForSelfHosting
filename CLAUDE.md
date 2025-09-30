# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 📑 Quick Reference Index

- [Critical System Rules](#critical-system-rules-) - **START HERE** - Core invariants for Movie Events, Phases, People, and Awards
- [Essential Commands](#essential-commands) - Build, test, and run commands
- [Architecture Overview](#architecture-overview) - High-level system design
- [Development Workflow](#development-workflow) - Before/during/after development checklist
- [Testing Strategy](#testing-strategy) - Test execution sequences
- [Common Pitfalls](#common-pitfalls-) - Consolidated warnings and best practices

## 🚨 CRITICAL SYSTEM RULES ⚠️

**These are immutable invariants. Violations WILL cause system failures.**

### Cache-First Architecture

**The Single Source of Truth:** `PersonAssignmentCacheService` generates ALL person assignments at startup (20 years, Dictionary<DateTime, string>). Phase numbers are **computed values** via `PhaseCalculator`, not database lookups.

**Key Services:**
- **PersonAssignmentCacheService** (Singleton): Pre-computed rotation assignments
- **PhaseCalculator** (Static): Calculates phase numbers from cache data
- **TimelineRenderingService**: Builds timeline from cache + database enrichment

### Movie Events

**What it is:** Primary entity for a movie session (one month, one person, one movie).

**Database:** MongoDB collection "MovieEvents"

**Critical Rules:**
- `PhaseNumber` is a computed value (via PhaseCalculator), not a traditional foreign key
- Created one per month with assigned person from PersonAssignmentCache
- Awards months have NO MovieEvent (use AwardEvent entities)
- File: `MovieReviewApp/Models/MovieEvent.cs`

### Phases

**What it is:** Logical grouping of movie events (one complete rotation through all people).

**Database:** MongoDB collection "Phases" - **OPTIONAL** (demo instances only)

**Architecture Decision:**
```csharp
[MongoCollection("Phases")]
public class Phase : BaseModel
{
    public int Number { get; set; }              // Computed by PhaseCalculator
    [BsonIgnore]                                  // NEVER persisted to database
    public List<MovieEvent> Events { get; set; } // Populated at runtime only
    public string People { get; set; }           // CSV: "Dave,Jared,Lacey..."
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
```

**Current Design (Cache-First):**
- Phase table is **legacy** - NOT required for production
- Phase boundaries calculated from cache via TimelineRenderingService
- Phase.Events populated at runtime by querying MovieEvents
- Demo instances populate Phase table for reference only
- Awards months create gaps BETWEEN phases, not within them

**Files:**
- Model: `MovieReviewApp/Models/Phase.cs`
- Calculator: `MovieReviewApp/Utilities/PhaseCalculator.cs`
- Demo creation: `MonthlyDataGenerationService.cs:265-279`

### Person Rotation Algorithm

**What it is:** Deterministic assignment generator (20 years, pre-computed at startup).

**Two Modes:**
1. **Ordered** (RespectOrder=true): Sequential rotation by Person.Order field
2. **Random** (RespectOrder=false): Pool-based with fixed seed (1337)

**Immutable Rules:**

1. **ALWAYS sort by Order field** (NEVER by Name):
```csharp
people = allPeople.OrderBy(p => p.Order).ToList();  // NEVER change
```

2. **Fixed Random seed** (deterministic across restarts):
```csharp
Random timelineRand = new Random(1337);  // NEVER change
```

3. **Pool refill maintains Order field sequence**:
```csharp
if (pool.Count == 0) {
    pool = peopleNames.ToList();  // Same order every phase
}
```

4. **Consecutive months at phase boundaries CAN have same person** (NOT a bug):
   - Phase 1 ends: Dave (random from pool)
   - Phase 2 starts: New pool → Could also select Dave

5. **NO simulation loops** - cache contains ALL assignments in single pass

**Files:**
- Cache: `PersonAssignmentCacheService.cs`
- Algorithm: `PersonRotationService.cs`
- Tests: `PersonRotationServiceTests.cs`, `ConsecutiveSelectionValidationTests.cs`

### Awards System

**What it is:** Gaps BETWEEN phases (no person assignment, no MovieEvent).

**Rules:**
- Cache markers: `"Awards Event 1"`, `"Awards Event 2"`, etc.
- Created AFTER phase completes (e.g., after 2 phases if PhasesBeforeAward=2)
- AwardEvent.PhaseNumber = completed phase number
- Phase numbers remain sequential (no gaps)

**Timeline (6 people, PhasesBeforeAward=2):**
```
Phase 1: Mar-Aug 2024  (6 months)
Phase 2: Sep 2024-Feb 2025 (6 months)
Awards: Mar 2025 ← Gap (PhaseNumber=2)
Phase 3: Apr-Sep 2025 (6 months)
```

**Files:**
- Model: `AwardEvent.cs`
- Settings: `AwardSettings.cs`
- Service: `AwardEventService.cs`

---

## Essential Commands

### Development
```bash
# Navigate to project directory FIRST
cd MovieReviewApp

# Build and run
dotnet restore
dotnet build
dotnet run --instance "dev"        # Auto-assigns port
dotnet run --instance "dev" --port 5000

# Instance management
dotnet run --list
dotnet run --help
```

### Testing
```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~PersonRotationServiceTests"
```

### Code Quality
```bash
# Build with strict enforcement
dotnet build -warnaserror

# Format validation
dotnet format --verify-no-changes
```

**Code Standards (Enforced as Errors):**
- No `var` keyword - explicit types required: `string name = "value"`
- File-scoped namespaces: `namespace MovieReviewApp;`

---

## Architecture Overview

**Blazor Server** application (.NET 9.0) for movie discussion groups with audio transcription and AI analysis.

### Solution Structure
- `MovieReviewApp/` - Main Blazor Server application
- `MovieReviewApp.Tests/` - xUnit test project
- `Application/Services/` - Business logic layer

### Core Patterns

1. **Multi-Instance Design**: Each movie group = isolated instance with own MongoDB database
   - Database naming: `MovieReviewApp_{instanceName}`
   - Configuration: `InstanceManager` and `InstanceService`

2. **Clean Architecture Layers**:
   - **Controllers**: HTTP endpoints (file uploads, API)
   - **Services**: Business logic (Analysis, Processing, Person management)
   - **Infrastructure**: Database (MongoDB + GridFS), FileSystem, Configuration
   - **Components**: Blazor UI (Pages/Partials)

3. **Audio Processing Pipeline**: Upload → Conversion → Transcription (Gladia API) → AI Analysis → Storage

4. **Key Services**:
   - `InstanceManager`: Multi-instance isolation
   - `SecretsManager`: Encrypted API keys
   - `PersonAssignmentCacheService`: 20-year rotation cache (Singleton)
   - `PhaseCalculator`: Static phase number computation
   - `TimelineRenderingService`: Cache-first timeline builder
   - `DemoProtectionService`: Read-only mode

### Startup Sequence (Program.cs) - ORDER IS IMMUTABLE

```csharp
// 1. MongoDB Serialization (BEFORE any DB operations)
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));

// 2. Instance Management (determines DB name)
InstanceManager instanceManager = new InstanceManager(instanceName);

// 3. Security (BEFORE service registration)
DemoProtectionService demoProtection = new DemoProtectionService(instanceManager);
SecretsManager secretsManager = new SecretsManager(instanceManager, demoProtection);

// 4. DI Registration (ORDER CRITICAL - dependencies flow downward)
builder.Services.AddSingleton(instanceManager);            // Infrastructure
builder.Services.AddSingleton(demoProtection);
builder.Services.AddSingleton(secretsManager);
builder.Services.AddSingleton<MongoDbService>();           // Database
builder.Services.AddSingleton<PersonAssignmentCacheService>(); // Cache (needs DB)
builder.Services.AddScoped(typeof(IRepository<>), typeof(MongoRepository<>)); // Repository layer
builder.Services.AddScoped<PersonService>();               // Domain services
builder.Services.AddScoped<MovieEventService>();
builder.Services.AddScoped<HomePageDataService>();         // Business logic
builder.Services.AddHostedService<MonthlyDataGenerationService>(); // Background LAST
```

**Why Order Matters:** InstanceManager → DB name, MongoDbService → Cache initialization, Repository → Domain services

---

## Development Workflow

### Before Making Changes
```bash
cd MovieReviewApp
dotnet restore
dotnet build -warnaserror
dotnet test
```

### During Development
```bash
dotnet build -warnaserror
dotnet test --filter "FullyQualifiedName~[RelevantTestClass]"
dotnet format --verify-no-changes
```

### Before Committing
```bash
dotnet test
dotnet build -c Release -warnaserror
dotnet run --instance "dev"  # Verify changes work
```

---

## Testing Strategy

### Critical Test Categories
```bash
# 1. Rotation Algorithm (determinism validation)
dotnet test --filter "PersonRotationAlgorithmTests"
dotnet test --filter "ConsecutiveSelectionValidationTests"

# 2. Phase Calculator
dotnet test --filter "PhaseCalculator"

# 3. Timeline Integration
dotnet test --filter "TimelineImplementationTests"
```

---

## Common Pitfalls ⚠️

### Cache & Phase Management
- ❌ **DO NOT** create simulation loops (cache contains ALL assignments)
- ❌ **DO NOT** query Phase table for production logic (use PhaseCalculator)
- ❌ **DO NOT** sort people by Name (MUST be Order field)
- ❌ **DO NOT** change Random seed (1337 = deterministic)
- ✅ **ALWAYS** use PersonAssignmentCacheService (single source of truth)
- ✅ **ALWAYS** use PhaseCalculator for phase numbers

### Dependency Injection
- ❌ **DO NOT** manually create services (memory leaks, no DI benefits)
- ✅ **ALWAYS** use constructor injection

**Anti-pattern:**
```csharp
// ❌ NEVER DO THIS
var logger = new LoggerFactory().CreateLogger<SettingService>();
var service = new SettingService(_db, logger, demoProtection);
```

**Correct:**
```csharp
// ✅ Constructor injection
public HomePageDataService(SettingService settingService) { ... }
```

### General
- ❌ **NEVER** modify Program.cs service registration order
- ❌ **NEVER** use `var` keyword (enforced as build error)
- ✅ **ALWAYS** run tests before committing
- ✅ **ALWAYS** use MongoDbService for data access (instance isolation)

---

## Recent Changes (2025-09-29/30)

**Architectural Shift:**
- Phase table → Legacy (demo only)
- PhaseCalculator → Static computation (production)
- TimelineRenderingService → Cache-first timeline builder
- Removed ~1,500 lines of unused abstraction layers

**Impact:**
- O(1) cache lookups vs O(n) database queries
- No Phase table dependency for production
- Deterministic behavior with fixed Random seed (1337)

---

## Key File Locations

### Core Models
- `Models/MovieEvent.cs` - Movie session entity
- `Models/Phase.cs` - Phase container (legacy/demo)
- `Models/Person.cs` - Person with Order field
- `Models/AwardEvent.cs` - Awards month entity

### Critical Services
- `Services/PersonAssignmentCacheService.cs` - 20-year cache (Singleton)
- `Services/PersonRotationService.cs` - Rotation algorithm (static)
- `Services/TimelineRenderingService.cs` - Cache-first timeline builder
- `Services/MovieEventService.cs` - Movie event logic
- `Utilities/PhaseCalculator.cs` - Static phase number computation

### UI
- `Components/Pages/Home.razor.cs` - Timeline display
- `Components/Partials/TimelineItemRenderer.razor` - Timeline rendering

### Tests
- `Tests/PersonRotationServiceTests.cs` - Algorithm validation
- `Tests/ConsecutiveSelectionValidationTests.cs` - Regression tests
- `Tests/TimelineImplementationTests.cs` - Timeline integration

### Config
- `Program.cs` - Startup & DI (ORDER IMMUTABLE)
- `appsettings.json` - Config (never commit API keys)
- `.editorconfig` - Code quality (no var)

---

## Additional Notes

### Audio Processing
- State machine in `AudioProcessingService`
- Workflow: Upload → Conversion (if needed) → Transcription → Analysis → Storage
- WAV files >100MB automatically converted to MP3 using FFmpeg
- Real-time status updates via SignalR

### Themes
- `ThemeService` manages 14 themes (7 base × 2 variants)
- CSS variables in theme-specific stylesheets
- Both dark and light variants supported

### Security
- API keys encrypted per instance in `secrets/` directory
- Never commit actual API keys
- Configuration loaded through `SecureConfigurationProvider`
- Demo instances enforce read-only mode via `DemoProtectionService`