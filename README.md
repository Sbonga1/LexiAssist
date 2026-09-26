# LexiAssist

LexiAssist is an ASP.NET Core MVC application for educational screening and learning support. It lets learners complete configured screening activities, review stored results, use assigned practice activities, and access educational resources. Administrators manage users and content. Psychologists review referrals that are assigned to them.

**LexiAssist is a screening and learning-support platform. Automated screening indicators and risk tiers are not a clinical diagnosis. Professional assessment remains the responsibility of appropriately qualified professionals.**

## Technology stack

- .NET 10 and ASP.NET Core 10
- ASP.NET Core MVC with Razor Views
- Entity Framework Core 10
- SQLite for development
- ASP.NET Core Identity with Entity Framework Core
- Bootstrap and vanilla JavaScript
- xUnit and ASP.NET Core integration testing

The application is intentionally a simple ASP.NET Core MVC monolith. It does not use Clean Architecture projects, CQRS, MediatR, a repository layer, microservices, message buses, Docker, or an external API provider.

## Prerequisites

- .NET 10 SDK
- PowerShell, Bash, or another shell
- A browser with JavaScript enabled
- The local dotnet-ef tool restored from dotnet-tools.json for migration commands

Audio recording additionally requires a browser that supports MediaRecorder and user permission for the microphone. Recording is optional.

## Clone and set up

Replace <repository-url> with the repository URL supplied for your environment:

~~~powershell
git clone <repository-url> LexiAssist
cd LexiAssist
dotnet tool restore
dotnet restore
dotnet build
~~~

## Database and migrations

The development connection string is Data Source=lexiassist.db;Cache=Shared, configured as ConnectionStrings:DefaultConnection in src/LexiAssist.Web/appsettings.Development.json. Non-development environments must supply their own connection string through deployment configuration.

Development startup applies pending migrations automatically and idempotently ensures the Learner, Administrator, and Psychologist roles. The migrations seed the screening categories and the five learning activity definitions. They do not create learner, psychologist, or administrator accounts, and they do not add copyrighted assessment questions.

For explicit migration management:

~~~powershell
dotnet ef migrations list --project src/LexiAssist.Web
dotnet ef database update --project src/LexiAssist.Web
~~~

For deployment preparation, see [docs/deployment/DEPLOYMENT.md](docs/deployment/DEPLOYMENT.md), the [production configuration example](docs/deployment/production-environment.example.md), and the [deployment checklist](docs/deployment/DEPLOYMENT-CHECKLIST.md).

To recreate the local development database, stop the application, remove the SQLite database and its sidecar files, then apply the migrations again:

~~~powershell
Remove-Item src/LexiAssist.Web/lexiassist.db* -Force -ErrorAction SilentlyContinue
dotnet ef database update --project src/LexiAssist.Web
~~~

That removal is destructive to local development data. Migrations are intentionally kept as a sequence; they are not squashed.

## Start in development

The HTTP launch profile uses the Development environment and listens on the URL configured in launchSettings.json:

~~~powershell
dotnet run --project src/LexiAssist.Web --launch-profile http
~~~

The application applies development migrations during startup. In Development, local Data Protection keys are stored below App_Data/DataProtection-Keys so cookies and antiforgery tokens work without relying on a machine-wide key directory. The development logger uses the console provider.

## Development administrator

Administrator seeding is disabled by default. When it is needed for local development, keep the credentials in user-secrets rather than in checked-in files:

~~~powershell
dotnet user-secrets set "DevelopmentAdminSeed:Enabled" "true" --project src/LexiAssist.Web
dotnet user-secrets set "DevelopmentAdminSeed:Email" "admin@example.test" --project src/LexiAssist.Web
dotnet user-secrets set "DevelopmentAdminSeed:Password" "<unique-local-development-password>" --project src/LexiAssist.Web
~~~

The seed runs only in Development. Use a unique local password that satisfies the configured Identity rules. There is no shared or production administrator password in source control.

## Roles

- **Learner**: register publicly, complete available screening content, view personal reports, use assigned activities, view progress and published resources, and manage personal settings.
- **Administrator**: manage users and roles, configure screening tests and questions, view reports, assign referrals, manage educational resources, inspect alerts, and inspect the audit log.
- **Psychologist**: view only referrals assigned to that account, open the Diagnostic Data Passport, review stored category and error information, access authorized recordings, add professional notes, and update referral status.

Authorization and ownership checks are applied to protected routes. Public registration always assigns the Learner role and does not accept a requested elevated role.

## Project structure

~~~text
LexiAssist/
├── src/LexiAssist.Web/
│   ├── Controllers/              # MVC endpoints and authorization boundaries
│   ├── Data/                     # DbContext, initialization, migrations
│   ├── Models/
│   │   ├── Activities/
│   │   ├── Audit/
│   │   ├── Games/
│   │   ├── Identity/
│   │   ├── Notifications/
│   │   ├── Recordings/
│   │   ├── Referrals/
│   │   ├── Reports/
│   │   ├── Resources/
│   │   └── Screening/
│   ├── Services/                 # Scoring, reports, games, recommendations, etc.
│   │   └── Interfaces/
│   ├── ViewModels/               # Request and view-specific models
│   ├── Views/                    # Razor views and shared layout
│   └── wwwroot/
│       ├── css/
│       ├── js/
│       └── lib/
└── tests/LexiAssist.Tests/       # xUnit unit and integration tests
~~~

## Screening architecture

Screening is represented by a reusable ScreeningTest and ScreeningQuestion model. Each test can contain questions of different configured types instead of requiring a separate table for every assessment. The current question types include multiple choice, text response, word reading, non-word reading, phonological choice, timed naming, spelling, reading fluency, reading comprehension, and listening comprehension.

Learner data is stored as:

ScreeningAttempt → ScreeningTestAttempt → ScreeningResponse

The server chooses the next active question. Responses are stored before advancing, protected against duplicate submissions, and tied to the authenticated learner's attempt. Inactive tests and questions are not newly presented.

Administrators configure tests and questions, including answer lists, passages, timing metadata, audio references, and whether recording is permitted. The migration seed provides categories only; question content must be configured for a category to be available to learners.

## Scoring and support tiers

IScreeningScoringService scores objective responses using transparent configuration and question weights. The result contains raw score, available score, percentage, flagged skill areas, and completion status. Missing or unscorable responses are handled without inventing a score.

IScreeningRiskClassificationService applies configurable, deterministic thresholds. The checked-in defaults are:

- **Red**: below 50%
- **Amber**: 50% to below 75%
- **Green**: 75% or above

These are screening/support tiers that describe configured indicators. They are not clinical cutoffs and must not be presented as a diagnosis. Completed reports store the calculated tier, scoring/classification versions, summary, flagged areas, and calculation date so the report remains a historical snapshot when configuration changes later.

A Red result can create a referral recommendation. Administrators assign a Psychologist, and the Psychologist can access only assigned, authorized referrals. The application does not automatically notify external professionals.

## Learning and game architecture

Learning activities use shared LearningActivity, ActivityAssignment, and ActivityAttempt records. Recommendations map stored screening skill flags and error categories to activities through ActivityRecommendations:Mappings. Recommendations do not overwrite an administrator's or Psychologist's assignment decision.

The current practice services are:

- **Sound Builder** — configurable blending and segmentation chunks
- **Fast Name Race** — randomized 20-item naming grid with a configurable default duration
- **Word Reader** — configured words with optional playback and self-reported attempts
- **Syllable Slicer** — configured syllable chunks for complex words
- **Say-Write-Match** — configurable spelling chunks with optional pronunciation audio

Game content is currently configured in appsettings.json (SoundBuilder, FastNameRace, WordReader, SyllableSlicer, and SayWriteMatch). There is no administrator game-content editor yet. Each game service persists attempts and neutral metrics through ActivityAttempt.

Adaptive Support is deterministic practice-only support. For example, three consecutive incorrect practice items can lower difficulty for the next configured number of items before returning toward the assigned level. It does not infer emotion, intelligence, or a medical condition. Supportive feedback uses predefined templates. Practice stories use the deterministic template generator; no external AI provider is configured.

## Audio storage and access warning

Learner recordings are opt-in and available only for activities or screening questions that explicitly permit recording. The browser uses MediaRecorder where supported and shows consent, start/stop state, and a maximum duration.

Recordings are validated on the server for ownership, duration, size, content type, file signature, and permitted target. Filenames are generated by the server and stored below App_Data/PrivateAudioRecordings; they are not public static files. Playback is served through authorized controller actions and is not available by guessing a filesystem URL. The configured limits are 120 seconds and 4 MB per upload by default.

Optional pronunciation assets referenced by Word Reader and Say-Write-Match are development content under wwwroot/media. Do not place learner recordings or other sensitive material in that public static-content area.

Configured screening audio is resolved from the private App_Data/ScreeningAudio area, with extension and path checks. Missing or unsupported audio returns a useful application response. No speech recognition, transcription, or automated pronunciation evaluation is implemented.

## Accessibility and learner preferences

Authenticated learners can persist:

- warm cream, soft blue, muted gray, or standard light reading backgrounds;
- font scale;
- increased line spacing;
- reduced motion;
- optional learner interests;
- reminder preference and daily practice target.

The shared layout applies these preferences after login. Forms use labels and validation summaries, focus states are visible, layouts are responsive, and practice drag/drop interactions provide button or keyboard alternatives. Timers expose status to assistive technology where appropriate. Progress charts use stored data only and include text summaries and empty states.

## Configuration and secrets

Non-secret development defaults and transparent scoring/activity configuration are in src/LexiAssist.Web/appsettings.json. Override settings with environment variables or a deployment configuration provider. For example:

~~~text
ConnectionStrings__DefaultConnection=Data Source=lexiassist.db;Cache=Shared
~~~

Keep passwords, connection credentials, external service credentials, and production Data Protection keys outside source control. User-secrets are suitable for local Development; use the deployment platform's secret store in production. Do not put passwords, password hashes, tokens, or raw sensitive request bodies in logs.

## Testing

Run the full solution build and test suite from the repository root:

~~~powershell
dotnet build
dotnet test
~~~

The tests use isolated test databases and application factories rather than the developer's physical SQLite database. Coverage includes authentication, role authorization, registration, user management, screening attempts and responses, scoring, risk thresholds, report snapshots, referrals, activities and adaptive support, recording authorization, privacy boundaries, resources, alerts, audit logging, and error handling.

## Production considerations

- Use HTTPS and a production-grade database configuration rather than the development SQLite file where required by deployment scale.
- Apply migrations through an explicit deployment step. Automatic migration application is limited to the Development environment.
- Persist and protect the Data Protection key ring across instances and deployments.
- Store private recordings outside public static content, restrict access, set retention rules, and protect backups.
- Review screening questions, answer keys, thresholds, wording, and professional workflows before use with learners.
- Keep production administrator credentials and all other secrets in a managed secret store.
- Configure structured, access-controlled logging and review the audit log retention policy.
- Obtain the appropriate privacy, safeguarding, accessibility, and professional review for the deployment context.

## Known limitations

- The migration seed includes screening categories but no complete standardized assessment question bank. Administrators must configure appropriate content.
- Game content is configuration-based and does not yet have an administrator editing interface.
- Audio is optional and browser-dependent. There is no speech recognition, transcription, or clinical pronunciation scoring.
- There are no external authentication providers, JWT APIs, email/SMS notifications, background reminder jobs, attendance integration, or PDF report export.
- Practice stories are deterministic templates; external AI content generation is only represented by a provider-ready interface and is not connected.
- Risk tiers and progress summaries are educational support information and must be interpreted by appropriately qualified professionals.

