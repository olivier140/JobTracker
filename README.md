# JobTracker

Automated job scraping and AI-powered matching pipeline for Microsoft Careers. Scrapes open positions via the Microsoft Careers PCSX API, scores each against your resume using Claude AI, generates tailored resumes for strong matches, and tracks application status through a desktop UI.

## Architecture

```
JobTracker.sln
 |
 |-- JobTracker.Core          Class library - models, interfaces, scraper, matcher, DB context
 |-- JobTracker.Console       Console app   - runs the pipeline once and exits
 |-- JobTracker.Service       Worker service - runs the pipeline on a 24-hour schedule
 |-- JobTracker.WinForms      Desktop app   - GUI with dashboard, job grid, and application tracker
 |-- JobTracker.Tests          NUnit tests   - unit, functional, and API discovery tests
 |
 |-- appsettings.json          Shared configuration (linked into all projects at build time)
```

### Pipeline Flow

```
 1. SCRAPE                           2. SCORE                          3. TRACK
 ───────────────                     ─────────────                     ─────────────
 GET /api/pcsx/search            ──> Claude AI scores each job    ──> ApplicationRecord
   • query, location, pagination       • 1-10 score                    created if score
 GET /api/pcsx/position_details        • top matches / gaps            >= MinScoreToApply
   • full HTML job description         • tailored resume
 Deduplicate by JobId                    if score >= threshold
 Persist ScrapedJob to DB            Persist JobMatch to DB
```

All three entry points (Console, Service, WinForms) execute this same pipeline through shared `IJobScraper` and `IJobMatcher` interfaces registered via `ServiceRegistration.AddJobTrackerCore()`.

## Technologies

| Category | Technology | Version |
|----------|-----------|---------|
| Runtime | .NET | 10.0 |
| Language | C# | 14 |
| Database | SQL Server + Entity Framework Core | 10.0.3 |
| AI | Anthropic Claude (claude-sonnet-4-6) via Anthropic.SDK | 5.10.0 |
| Desktop UI | Windows Forms | .NET 10 |
| Service hosting | Microsoft.Extensions.Hosting.WindowsServices | 10.0.3 |
| DI / Config / Logging | Microsoft.Extensions.* | 10.0.3 |
| Testing | NUnit + Moq + EF Core InMemory | 4.3.2 / 4.20.72 / 10.0.3 |

## Projects

### JobTracker.Core

Shared class library containing all business logic. No entry point.

- **MicrosoftJobsScraper** (`IJobScraper`) -- Calls the Microsoft Careers PCSX API at `apply.careers.microsoft.com/api/pcsx/`. Initializes an HTTP session for cookies, searches positions with pagination, fetches full job descriptions, deduplicates by `JobId`, and persists `ScrapedJob` entities.
- **ClaudeJobMatcher** (`IJobMatcher`) -- Sends each unscored job + your resume to Claude for a 1-10 score with top matches and gaps. If the score meets the threshold, generates a tailored resume and creates an `ApplicationRecord` with a 14-day follow-up reminder.
- **ServiceRegistration** -- `AddJobTrackerCore(AppSettings)` extension method wires up all singletons (scraper, matcher, settings) and the `DbContextFactory`. `EnsureDatabaseAsync()` ensures the database exists on startup.
- **Models** -- API DTOs (`PcsxApiResponse<T>`, `PcsxPosition`, `PcsxPositionDetail`, `JobScore`) and EF Core entities (`ScrapedJob`, `JobMatch`, `ApplicationRecord`, `ApplicationEvent`).

### JobTracker.Console

Runs the scrape-and-score pipeline once and exits. Suitable for manual runs, cron jobs, or CI.

```
dotnet run --project JobTracker.Console
```

Returns exit code 0 on success, 1 on failure.

### JobTracker.Service

Windows background service that runs the pipeline on a configurable schedule (default: every 24 hours). Uses `BackgroundService` with `UseWindowsService()`.

```powershell
# Build
dotnet publish JobTracker.Service -c Release -o C:\Services\JobTracker

# Install (Administrator)
sc create JobTracker binPath="C:\Services\JobTracker\JobTracker.Service.exe" start=auto
sc description JobTracker "Scrapes Microsoft careers and scores jobs via Claude AI"
sc start JobTracker
```

The schedule interval is configured in `JobTracker.Service/appsettings.Service.json`.

### JobTracker.WinForms

Desktop application with five tabs:

| Tab | Purpose |
|-----|---------|
| Dashboard | Live log output during scrape/score runs |
| All Jobs | Searchable grid of all scraped positions |
| Matches | Color-coded scored jobs (green/yellow/red) with tailored resume viewer |
| Applications | Status tracker (Pending, Applied, Interviewing, Offer, Rejected) |
| Settings | Edit resume text in-app |

Toolbar buttons: Scrape Now, Score Unscored, Refresh. Stat counters across the top.

### JobTracker.Tests

18 tests across four files:

| File | Tests | Type |
|------|-------|------|
| ScraperWorkerTests | 7 | Unit (Moq mocks) |
| UpdateStatusTests | 3 | Integration (EF Core InMemory) |
| MicrosoftJobsScraperFunctionalTests | 7 | Functional (live API, skippable) |
| ApiDiscoveryTest | 1 | Discovery (validates API endpoints) |

```
dotnet test
```

Functional tests use `Assert.Ignore()` to gracefully skip when the Microsoft API is unavailable.

## Database Schema

Four tables with relational integrity managed by EF Core:

```
ScrapedJobs  1:1  JobMatches  1:1  Applications  1:N  ApplicationEvents
```

- **ScrapedJobs** -- `JobId` (unique), Title, Location, DescriptionFull, Url, PostedDate, ScrapedAt
- **JobMatches** -- Score (1-10), TopMatchesJson, GapsJson, RecommendApply, TailoredResume, EvaluatedAt
- **Applications** -- Status, AppliedAt, FollowUpAt, Notes
- **ApplicationEvents** -- EventType, Detail, OccurredAt (audit trail for status changes)

Database is auto-created on first run via `EnsureCreatedAsync()`.

## Configuration

A single `appsettings.json` at the solution root is linked into all projects at build time. The Service project layers `appsettings.Service.json` on top for the schedule interval.

```jsonc
// appsettings.json (solution root)
{
  "AppSettings": {
    "ConnectionString": "Server=localhost;Database=JobTracker;...",
    "SearchQuery": "senior software engineer",
    "SearchLocation": "United States",
    "MaxPages": 3,
    "MinScoreToApply": 7,
    "Resume": ""
  }
}
```

Two environment variables are required at runtime:

| Variable | Purpose |
|----------|---------|
| `ANTHROPIC_API_KEY` | Anthropic API key for Claude |
| `JOBTRACKER_RESUME` | Your resume text for job matching |

## Getting Started

### Prerequisites

- .NET 10 SDK
- SQL Server (LocalDB or full instance)
- Anthropic API key

### Run

```bash
# Set required environment variables
set ANTHROPIC_API_KEY=sk-ant-...
set JOBTRACKER_RESUME="Your resume text here..."

# Run once via console
dotnet run --project JobTracker.Console

# Or launch the desktop app
dotnet run --project JobTracker.WinForms

# Or run tests
dotnet test
```
