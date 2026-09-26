# LexiAssist production configuration example

Supply these values through the hosting platform's environment/configuration mechanism. The examples are placeholders only; do not copy them as credentials.

| Environment variable / key | Required | Secret | Purpose | Example format | Persistence implications |
| --- | --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Yes | No | Selects production behavior. | `Production` | None. |
| `ASPNETCORE_URLS` | Hosting-dependent | No | Listening URL(s) for the published process. | `http://127.0.0.1:5105` | None; coordinate with the hosting/proxy layer. |
| `ConnectionStrings__DefaultConnection` | Yes | No for the current SQLite provider | SQLite database connection string. | `Data Source=<persistent-db-path>/lexiassist.db;Cache=Shared` | The database file and SQLite sidecars must persist. |
| `AllowedHosts` | Yes | No | Host names accepted by ASP.NET Core host filtering. Do not use `*` in production. | `lexiassist.example.org` | None. |
| `DataProtection__KeyPath` | Yes | No, but the directory contains sensitive key material | Persistent absolute directory for ASP.NET Core Data Protection keys. | `<persistent-root>/data-protection` | The directory must persist across restarts and be private. |
| `LearnerAudioRecording__StorageDirectory` | Yes | No, but the files are sensitive | Persistent absolute directory for learner recordings. | `<persistent-root>/learner-recordings` | The directory must persist, be writable by the app, and never be web-accessible. |
| `ProductionAdminBootstrap__Enabled` | Optional; defaults to `false` | No | Enables one-time production administrator bootstrap. | `true` only for the controlled bootstrap start | Set back to `false` and remove the credential values after successful provisioning. |
| `ProductionAdminBootstrap__Email` | Required only when bootstrap is enabled | No | Email for the one-time administrator account. | `administrator@example.org` | Do not place it in source control. |
| `ProductionAdminBootstrap__Password` | Required only when bootstrap is enabled | Yes | One-time administrator password subject to Identity policy. | `<unique-high-entropy-password>` | Supply through a secret mechanism and remove after bootstrap. |
| `LearnerAudioRecording__MaxDurationSeconds` | Optional | No | Maximum recording duration. Current default is 120 seconds. | `120` | None. |
| `LearnerAudioRecording__MaxFileSizeBytes` | Optional | No | Maximum recording upload size. Current default is 4194304 bytes. | `4194304` | None. |
| `Logging__LogLevel__Default` | Optional | No | Baseline operational log level. | `Information` | Use the hosting platform's protected log destination and retention. |
| `Logging__LogLevel__Microsoft.AspNetCore` | Optional | No | Framework log level. | `Warning` | Use the hosting platform's protected log destination and retention. |

`DevelopmentAdminSeed:*` is development-only and must not be enabled in Production.

There is no configured email provider or external-service credential in the current application. Instructional screening audio has no separate configuration key: production assets are supplied in the persistent `App_Data/ScreeningAudio` directory beside the application content root, outside `wwwroot`.
