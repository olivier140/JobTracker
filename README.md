# JobTracker

Automated job scraping and AI-powered matching pipeline for Microsoft Careers. Scrapes open positions via the Microsoft Careers PCSX API, scores each against your resume using Claude AI, generates tailored resumes for strong matches, exports them as formatted Word documents, sends export notifications via NServiceBus for downstream processing, and tracks application status through a desktop UI.

## Architecture

```
JobTracker.sln
 |
 |-- JobTracker.Core          Class library  - models, interfaces, scraper, matcher, DB context
 |-- JobTracker.WordExport    Class library  - Word (.docx) resume export via OpenXML
 |-- JobTracker.Messaging     Worker service - NServiceBus endpoint that processes export commands
 |-- JobTracker.Console       Console app    - runs the pipeline once and exits; supports --export
 |-- JobTracker.Service       Worker service - runs the pipeline on a 24-hour schedule
 |-- JobTracker.WinForms      Desktop app    - GUI with dashboard, job grid, and application tracker
 |-- JobTracker.Tests         NUnit tests    - unit, functional, and API discovery tests
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

 4. EXPORT  (--export flag or AutoExportOnScore: true)
 ─────────────────────────────────────────────────────────────────────────
 ResumeExporter builds a .docx from TailoredResume + job metadata
 Saved to %USERPROFILE%\Documents\JobTracker\Resumes\  (or ExportPath)
 File name: Resume_{Title}_{JobId}_{yyyy-MM-dd}.docx

 5. NOTIFY  (NServiceBus command sent after each successful export)
 ─────────────────────────────────────────────────────────────────────────
 WinForms / Console send a ResumeExportedCommand via SQL Server transport
 JobTracker.Messaging endpoint receives and processes the command
 Bus messages persisted in SQL Server under the [nsb] schema
```

All three entry points (Console, Service, WinForms) execute the same scrape-and-score pipeline through shared `IJobScraper` and `IJobMatcher` interfaces registered via `ServiceRegistration.AddJobTrackerCore()`. The export step is opt-in and uses `IResumeExporter` registered via `ServiceCollectionExtensions.AddWordExport()`. After each successful export, a `ResumeExportedCommand` is sent to the `JobTracker.Messaging` endpoint for downstream processing.

## Technologies

| Category | Technology | Version |
|----------|-----------|---------|
| Runtime | .NET | 10.0 |
| Language | C# | 14 |
| Database | SQL Server + Entity Framework Core | 10.0.3 |
| AI | Anthropic Claude (claude-sonnet-4-6) via Anthropic.SDK | 5.10.0 |
| Word generation | DocumentFormat.OpenXml | 3.2.0 |
| Messaging | NServiceBus + SQL Server Transport | 10.1.0 / 9.0.0 |
| Desktop UI | Windows Forms | .NET 10 |
| Service hosting | Microsoft.Extensions.Hosting.WindowsServices | 10.0.3 |
| DI / Config / Logging | Microsoft.Extensions.* | 10.0.3 |
| Testing | NUnit + Moq + EF Core InMemory | 4.3.2 / 4.20.72 / 10.0.3 |

## Projects

### JobTracker.Core

Shared class library containing all business logic. No entry point.

- **MicrosoftJobsScraper** (`IJobScraper`) — Calls the Microsoft Careers PCSX API at `apply.careers.microsoft.com/api/pcsx/`. Initializes an HTTP session for cookies, searches positions with pagination, fetches full job descriptions, deduplicates by `JobId`, and persists `ScrapedJob` entities.
- **ClaudeJobMatcher** (`IJobMatcher`) — Sends each unscored job + your resume to Claude for a 1-10 score with top matches and gaps. If the score meets the threshold, generates a tailored resume and creates an `ApplicationRecord` with a 14-day follow-up reminder.
- **ServiceRegistration** — `AddJobTrackerCore(AppSettings)` extension method wires up all singletons (scraper, matcher, settings) and the `DbContextFactory`. `EnsureDatabaseAsync()` ensures the database exists on startup. `EnsureNsbSchemaAsync()` creates the `nsb` schema for NServiceBus queue tables.
- **Models** — API DTOs (`PcsxApiResponse<T>`, `PcsxPosition`, `PcsxPositionDetail`, `JobScore`) and EF Core entities (`ScrapedJob`, `JobMatch`, `ApplicationRecord`, `ApplicationEvent`). `AppSettings` holds all runtime configuration including the export settings.
- **Commands** — `ResumeExportedCommand` is a plain POCO in the `JobTracker.Core.Commands` namespace. It carries job, match, and export data without any NServiceBus dependency, identified as a command via unobtrusive conventions.

### JobTracker.WordExport

Class library that converts a `JobMatch.TailoredResume` (plain text) into a formatted `.docx` Word document using the OpenXML SDK. No dependency on Windows or Office — works headlessly in the Console and Service projects.

- **`IResumeExporter`** — Two overloads: accepts a `(JobMatch, ScrapedJob)` pair directly, or looks up a match by `jobMatchId` from the database.
- **`ResumeExporter`** — Resolves the output directory, sanitizes the filename, streams the document to disk, and returns an `ExportResult(Success, FilePath, Error)`.
- **`ResumeDocumentBuilder`** *(internal)* — Pure OpenXML document assembly. Parses resume text line by line to identify the candidate name (large, centred, dark blue), contact info (centred, small), section headings (bold, blue, with a bottom rule), and body text. Appends a *Match Summary* block at the end with job title, score, top matches, gaps, and generation timestamp.
- **`ServiceCollectionExtensions`** — `services.AddWordExport()` registers `IResumeExporter` as a singleton. Requires `AddJobTrackerCore()` to have been called first.

#### Output location

| Setting | Behaviour |
|---------|-----------|
| `ExportPath` empty (default) | `%USERPROFILE%\Documents\JobTracker\Resumes\` |
| `ExportPath` set | The configured path |

File naming: `Resume_{SafeTitle}_{JobId}_{yyyy-MM-dd}.docx`

The output directory is created automatically if it does not exist.

### JobTracker.Messaging

NServiceBus receiver endpoint that processes `ResumeExportedCommand` messages sent after resume exports. Runs as a .NET Worker Service using `Host.CreateApplicationBuilder()` with `NServiceBus.Extensions.Hosting`.

- **Transport** — SQL Server transport with all queue tables in the `[nsb]` schema, keeping bus infrastructure separate from the application's `[dbo]` schema.
- **Conventions** — Unobtrusive message conventions identify commands by namespace (`JobTracker.Core.Commands`), so `JobTracker.Core` has no dependency on NServiceBus packages.
- **`ResumeExportedHandler`** — Implements `IHandleMessages<ResumeExportedCommand>`. Currently logs the received command data. Extend this handler with downstream business logic such as email notifications, external API calls, analytics, or workflow triggers.
- **Installers** — `EnableInstallers()` auto-creates NServiceBus queue tables on startup. The `nsb` schema itself is created by `ServiceRegistration.EnsureNsbSchemaAsync()` before the endpoint starts.

```bash
# Run the messaging endpoint
dotnet run --project JobTracker.Messaging
```

### JobTracker.Console

Runs the scrape-and-score pipeline once and exits. Suitable for manual runs, cron jobs, or CI.

```bash
# Standard run
dotnet run --project JobTracker.Console

# Run and export all qualifying tailored resumes to Word
dotnet run --project JobTracker.Console -- --export
```

The `--export` flag is equivalent to setting `AutoExportOnScore: true` in `appsettings.json`. Exports every `JobMatch` whose `TailoredResume` is populated and whose `Score >= MinScoreToApply`. After each successful export, a `ResumeExportedCommand` is sent to the `JobTracker.Messaging` endpoint via a send-only NServiceBus endpoint.

Returns exit code 0 on success, 1 on failure.

### JobTracker.Service

Windows background service that runs the pipeline on a configurable schedule (default: every 24 hours). Uses `BackgroundService` with `UseWindowsService()`. Respects `AutoExportOnScore` from configuration.

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
| Matches | Color-coded scored jobs (green/yellow/red) with tailored resume viewer and **Export to Word** button |
| Applications | Status tracker (Pending, Applied, Interviewing, Offer, Rejected) |
| Settings | Edit resume text in-app |

Toolbar buttons: **Scrape Now**, **Score Unscored**, **Refresh**. Stat counters across the top.

**Export to Word** — select any row in the Matches tab and click *Export to Word* to write the tailored resume to a `.docx` file. After a successful export, a `ResumeExportedCommand` is automatically sent to the messaging bus. A dialog confirms the file path and offers to open the containing folder in Explorer.

### JobTracker.Tests

38 tests across six files:

| File | Tests | Type |
|------|-------|------|
| ScraperWorkerTests | 7 | Unit (Moq mocks) |
| UpdateStatusTests | 3 | Integration (EF Core InMemory) |
| MicrosoftJobsScraperFunctionalTests | 7 | Functional (live API, skippable) |
| ApiDiscoveryTest | 1 | Discovery (validates API endpoints) |
| ResumeExporterTests | 14 | Unit (OpenXML document structure, filename sanitization) |
| ResumeExportedHandlerTests | 6 | Unit (handler logging, Moq mocks) |

```
dotnet test
```

Functional tests use `Assert.Ignore()` to gracefully skip when the Microsoft API is unavailable. `ResumeExporterTests` exercises `ResumeDocumentBuilder` directly via `InternalsVisibleTo` and verifies that the produced stream is a valid OpenXML document. `ResumeExportedHandlerTests` verifies the NServiceBus handler logs all expected command data.

## Database Schema

Four tables with relational integrity managed by EF Core, plus NServiceBus queue tables in a separate schema:

```
ScrapedJobs  1:1  JobMatches  1:1  Applications  1:N  ApplicationEvents    [dbo] schema

NServiceBus queue tables (auto-created by EnableInstallers)                 [nsb] schema
```

- **ScrapedJobs** — `JobId` (unique), Title, Location, DescriptionFull, Url, PostedDate, ScrapedAt
- **JobMatches** — Score (1-10), TopMatchesJson, GapsJson, RecommendApply, TailoredResume, EvaluatedAt
- **Applications** — Status, AppliedAt, FollowUpAt, Notes
- **ApplicationEvents** — EventType, Detail, OccurredAt (audit trail for status changes)
- **NServiceBus queues** — `[nsb].JobTracker.Messaging`, `[nsb].JobTracker.WinForms`, `[nsb].JobTracker.Console` (auto-created)

Database is auto-created on first run via `EnsureCreatedAsync()`. The `nsb` schema is created via `EnsureNsbSchemaAsync()`.

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
    "Resume": "",

    // Word export
    "ExportPath": "",           // Leave empty to use ~/Documents/JobTracker/Resumes/
    "AutoExportOnScore": false  // Set true to auto-export in Console and Service
  }
}
```

Two environment variables are required at runtime:

| Variable | Purpose |
|----------|---------|
| `ANTHROPIC_API_KEY` | Anthropic API key for Claude |
| `JOBTRACKER_RESUME` | Your resume text for job matching |

## NServiceBus Messaging

The messaging infrastructure uses NServiceBus with SQL Server transport. All bus-related tables are stored in the `[nsb]` schema to keep them separate from the application's EF Core schema.

### Endpoints

| Endpoint | Type | Purpose |
|----------|------|---------|
| `JobTracker.Messaging` | Full (receiver) | Processes `ResumeExportedCommand` messages |
| `JobTracker.WinForms` | Send-only | Sends commands after UI-triggered exports |
| `JobTracker.Console` | Send-only | Sends commands after CLI-triggered exports |

### Message Flow

```
WinForms / Console  ──send──>  [nsb].JobTracker.Messaging  ──handle──>  ResumeExportedHandler
   (send-only)                    (SQL Server queue)                       (logs + future logic)
```

### Conventions

Commands are identified by namespace convention (`type.Namespace == "JobTracker.Core.Commands"`) rather than marker interfaces. This keeps `JobTracker.Core` free of any NServiceBus package dependency.

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

# Run and export tailored resumes to Word
dotnet run --project JobTracker.Console -- --export

# Launch the desktop app
dotnet run --project JobTracker.WinForms

# Start the messaging endpoint (processes export notifications)
dotnet run --project JobTracker.Messaging

# Run tests
dotnet test
```
