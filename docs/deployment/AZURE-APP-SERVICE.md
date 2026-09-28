# Azure App Service deployment

LexiAssist keeps SQLite for local Development and uses Azure SQL Database for non-Development environments. The Azure SQL schema has its own EF Core migration set. The web process does not migrate a production database during startup.

## Target resources

- Linux Azure App Service running .NET 10 on a paid App Service plan.
- Azure SQL Database, reachable from the app through a private endpoint and App Service VNet integration where available. If using the SQL public endpoint, restrict its firewall to the app's outbound IP addresses and the operator's deployment IP; review those rules whenever the plan or scale configuration changes.
- A system-assigned managed identity for the App Service. Create a contained Azure SQL user for it and grant only `db_datareader` and `db_datawriter`. Use a separate deployment identity to apply schema changes.
- App Service persistent home storage for private audio and Data Protection keys. Set `WEBSITES_ENABLE_APP_SERVICE_STORAGE=true`. The app writes these to separate configured directories under `/home/lexiassist`; do not put SQLite there.
- A Key Vault reference for the OpenAI key, if AI question generation is enabled. Grant the web app identity access only to the required secret. Never put the key in source control, a publish profile, or a normal app setting value.

App Service's Linux home directory persists, but its availability and sharing depend on the App Service storage setting. The app therefore requires explicit absolute paths for all persistent file data outside Development. Azure Files mounts can be used for these files if required by the operations policy, but must not hold SQLite: Microsoft's App Service guidance explicitly excludes file-lock-based databases such as SQLite from storage mounts.

## App Service configuration

Set these application settings (replace host names and resource names):

| Setting | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AzureAppService__ForwardedHeaders` | `true` only when hosted behind the Azure App Service frontend. |
| `Database__Provider` | `SqlServer` |
| `ConnectionStrings__DefaultConnection` | `Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;Connect Timeout=30;` |
| `AllowedHosts` | `<app-name>.azurewebsites.net` and any configured custom host, separated by semicolons. Never use `*`. |
| `DataProtection__KeyPath` | `/home/lexiassist/data-protection` |
| `LearnerAudioRecording__StorageDirectory` | `/home/lexiassist/private-audio` |
| `ScreeningAudio__StorageDirectory` | `/home/lexiassist/screening-audio` |
| `QuestionImages__StorageDirectory` | `/home/lexiassist/question-images` |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE` | `true` |
| `OpenAI__ApiKey` | A Key Vault reference such as `@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/<secret-name>/<version>)`, if AI generation is enabled. |

App Service terminates public TLS before forwarding requests to the app. `AzureAppService__ForwardedHeaders=true` tells ASP.NET Core to process the single forwarded proxy hop so HTTPS redirection and Secure cookies work. This setting is specifically for the App Service frontend; do not enable it on an app exposed directly to untrusted clients.

Configure the App Service to accept HTTPS only, require TLS 1.2 or later, disable FTP, enable Always On and configure the `/health` health check. Choose an App Service plan size based on expected traffic and required deployment slots; no paid-tier size is assumed by the source configuration.

## Database setup and migrations

For a new Azure SQL database, generate and review the provider-specific migration script before applying it:

```powershell
dotnet tool restore
$env:Database__Provider = "SqlServer"
$env:ConnectionStrings__DefaultConnection = "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Default;Connect Timeout=30;"
dotnet ef migrations script --idempotent --project src/LexiAssist.Web --context LexiAssist.Web.Data.AzureSqlApplicationDbContext --output azure-sql-migrations.sql
```

Review the output, apply it to a staging database first, and then apply the approved script to production using a separately authorized migration identity. The app identity should not receive database DDL permissions. Do not let multiple web instances race to apply migrations. The health endpoint checks database connectivity; deployment should not start until the migration has completed.

Azure SQL starts empty with the current initial migration. This setup does not copy data from an existing SQLite database. If the application already contains production users, assessments, attempts, referrals, or audit records, prepare and verify a separate data migration before switching its connection string.

For each future model change, create and review migrations for both contexts:

```powershell
dotnet ef migrations add <Name> --project src/LexiAssist.Web --context LexiAssist.Web.Data.ApplicationDbContext
dotnet ef migrations add <Name> --project src/LexiAssist.Web --context LexiAssist.Web.Data.AzureSqlApplicationDbContext --output-dir Data/Migrations/AzureSql
```

The Azure SQL factory uses `ConnectionStrings__DefaultConnection` when scripts are applied. Keep the Azure SQL snapshot and embedded SQL resources in sync with each Azure SQL migration. Never edit an already-applied migration; add a new one.

## First administrator and account confirmation

If no administrator exists, set `ProductionAdminBootstrap__Enabled=true`, `ProductionAdminBootstrap__Email`, and a unique high-entropy `ProductionAdminBootstrap__Password` through Key Vault-backed settings for one controlled startup. Confirm the account can sign in, then disable bootstrap and remove the password setting.

Production requires confirmed email addresses, but the application currently has no email delivery provider or email-confirmation workflow. Administrator-created and bootstrapped accounts are confirmed directly; public self-registration does not send a confirmation email and cannot complete sign-in. Treat self-service registration as unavailable until an approved mail provider and confirmation flow are added. Do not loosen the confirmation requirement solely to make signup appear successful.

## Operational checks

- Enable App Service console/application logging with access and retention restricted to the operations team.
- Verify `/health` through the public HTTPS hostname after migrations and before directing users to the app.
- Test login, learner, administrator, and psychologist permissions in staging.
- Test audio upload and playback after an App Service restart, and confirm data protection keys persist across restart and scale events.
- Back up Azure SQL and the App Service persistent files. Restore into a non-production environment and verify audio rows still resolve to their files.
- Configure and rotate the OpenAI key in Key Vault only after credentials and privacy handling are approved.
- Do not deploy from this repository until the outstanding data-migration, mail, and live Azure smoke-test decisions are resolved.
