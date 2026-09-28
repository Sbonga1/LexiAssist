# LexiAssist deployment guide

This guide prepares the accepted LexiAssist application for a hosting-platform-neutral deployment. It does not assume IIS, Nginx, Docker, Kubernetes, a cloud provider, or a particular operating system.

## Prerequisites

- .NET 10 runtime for the framework-dependent publish output.
- A persistent SQL Server database. Azure SQL uses the SQL Server provider.
- Persistent private storage for learner recordings.
- Persistent private storage for Data Protection keys.
- HTTPS at the public origin.
- Production configuration from [production-environment.example.md](production-environment.example.md).
- Production instructional listening audio, if the configured screening content requires it.

## Configuration

Set the required values in [production-environment.example.md](production-environment.example.md) before starting the application. Set `AllowedHosts` to `*` in App Service configuration to accept requests sent to any hostname. The application refuses non-development startup when the database connection string, `AllowedHosts`, Data Protection key path, or learner recording path is missing or unsafe.

Do not enable `DevelopmentAdminSeed` in Production. If a first administrator is needed, use the explicitly enabled `ProductionAdminBootstrap` settings for one controlled startup, then disable the setting and remove the password from the hosting configuration.

## Database preparation

Configure the connection string under the exact name `DefaultConnection`. For Azure App Service, use the Connection strings section and select type `SQLAzure`. The SQL Server provider is used in every environment. For passwordless Azure SQL access, configure the App Service system-assigned managed identity and use `Authentication=Active Directory Managed Identity` in the connection string.

Enable the App Service system-assigned identity, then create its Azure SQL database user as an Entra administrator:

```sql
CREATE USER [<app-service-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<app-service-name>];
ALTER ROLE db_datawriter ADD MEMBER [<app-service-name>];
ALTER ROLE db_ddladmin ADD MEMBER [<app-service-name>];
```

Replace the placeholder with the exact App Service name. The application reads only `ConnectionStrings:DefaultConnection`; the App Service connection string overrides the LocalDB value from `appsettings.Development.json` in Azure.

The application does not apply EF Core migrations automatically outside Development. Use this sequence:

1. Stop or isolate the current application version.
2. Back up the target Azure SQL database.
3. Supply the Production connection string and the required Production startup configuration.
4. Apply migrations with the project's EF Core tool:

```text
dotnet ef database update --project src/LexiAssist.Web --startup-project src/LexiAssist.Web --context ApplicationDbContext -- --environment Production
```

The baseline migration creates the SQL Server schema. Existing local development data is not copied.

5. Start the published application.
6. Verify `/health`, authentication, and the role smoke tests.

When using a shell that does not inherit environment variables automatically, use its equivalent syntax. The migration command must point to the same persistent database file that the application will use.

## Persistent storage

Recommended logical layout:

```text
<application-artifact>/                 published application files
<persistent-root>/                       Azure SQL database (managed by Azure)
<persistent-root>/learner-recordings/   private learner recordings
<persistent-root>/data-protection/      ASP.NET Core Data Protection keys
<application-artifact>/App_Data/ScreeningAudio/  production instructional audio
<host-managed-log-location>/            protected application logs
```

The database, learner recordings, and Data Protection keys must survive application replacement. The learner-recording and Data Protection directories must be writable by the application process and must not be exposed as static web content. `App_Data/ScreeningAudio` is resolved through an authenticated controller route and must not contain personal recordings.

The development fixture `development-listening-blue-bird.wav` is not a clinical assessment asset. Exclude it from production content and supply separately approved instructional audio where required.

## Publish

From the repository root:

```text
dotnet publish src/LexiAssist.Web/LexiAssist.Web.csproj --configuration Release --output <temporary-publish-directory>
```

The command is framework-dependent because it does not pass `--self-contained` and the project targets `net10.0`. Copy only the publish output to the application location. Do not copy the development database, private recordings, Data Protection keys, user-secrets files, or temporary test/browser artifacts.

## Start

Start the published application using the hosting platform's process mechanism, for example:

```text
dotnet <publish-directory>/LexiAssist.Web.dll
```

Set `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS` as required by the host, and the production configuration values before starting. The application does not assume port 5105.

## HTTPS and reverse proxy

Production enables HTTPS redirection and HSTS and marks authentication and antiforgery cookies Secure. If a reverse proxy terminates TLS, configure that proxy and its forwarded scheme/host behavior according to the selected host. The application does not enable broad forwarded-header trust; do not trust arbitrary forwarded headers without a known proxy boundary.

The public origin must be HTTPS. A direct plain-HTTP Production request is not a substitute for that setup: the Secure antiforgery policy intentionally refuses to render sign-in forms when the request is not SSL. For a reverse-proxy deployment, validate the application through the HTTPS public origin (or through an internal request whose scheme has been correctly forwarded by the trusted proxy).

## Post-deployment validation

- `GET /health` returns a healthy response without revealing connection strings, paths, stack traces, or learner data.
- Over the deployed HTTPS origin, the login page renders and a provisioned administrator can sign in.
- Learner, administrator, and psychologist role smoke tests pass.
- Instructional audio plays through its protected route.
- An unauthorized user cannot access a private recording.
- Complete the manual microphone UAT in [DEPLOYMENT-CHECKLIST.md](DEPLOYMENT-CHECKLIST.md).

## Backup

Back up the production database, learner recordings, Data Protection keys, and production instructional audio/content that cannot be recreated. Store configuration secrets through the hosting platform's secure mechanism; do not place plaintext secrets in ordinary backup files.

## Restore

Restore an application-compatible database first, then the private learner recordings, Data Protection keys, production configuration, and instructional audio. Start the application only after those dependencies are present. Verify that database rows referring to recordings match the restored recording filenames and that `/health` and authentication work.

## Rollback

Rollback requires a compatible application artifact and compatible database schema. Stop the application, restore the last known-good artifact and configuration, and restore database/filesystem backups only when the schema and application version are compatible. The project does not claim zero-downtime deployment support or automatic schema rollback.

## Retention and policy decisions

Retention period requires organizational/policy decision.

The affected technical areas are learner recording files and rows, screening reports, referrals, and audit events. No arbitrary deletion period is configured by this guide.
