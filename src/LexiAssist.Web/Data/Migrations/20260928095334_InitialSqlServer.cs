using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    PreferredName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScreeningPrivacyNoticeAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EducationalResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: 12000, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationalResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActivityType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowsAudioRecording = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningTests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningTests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipientUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RelatedUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppNotifications_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LearnerInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    InterestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerInterests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerReminderPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PreferredReminderTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    DailyPracticeTargetMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerReminderPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerReminderPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PracticeStories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Theme = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TargetPhoneticPattern = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TargetWordsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DifficultyLevel = table.Column<int>(type: "int", nullable: false),
                    StoryText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    GenerationMethod = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeStories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeStories_AspNetUsers_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAccessibilityPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BackgroundTheme = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FontScale = table.Column<double>(type: "float", nullable: false),
                    IncreasedLineSpacing = table.Column<bool>(type: "bit", nullable: false),
                    ReduceMotion = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccessibilityPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAccessibilityPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScreeningTestId = table.Column<int>(type: "int", nullable: false),
                    IsCsvImport = table.Column<bool>(type: "bit", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDiscarded = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentDrafts_ScreeningTests_ScreeningTestId",
                        column: x => x.ScreeningTestId,
                        principalTable: "ScreeningTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OverallScore = table.Column<double>(type: "float", nullable: true),
                    RiskTier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RiskCalculationVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RiskCalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentTestId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningAttempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningAttempts_ScreeningTests_CurrentTestId",
                        column: x => x.CurrentTestId,
                        principalTable: "ScreeningTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScreeningTestId = table.Column<int>(type: "int", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    QuestionType = table.Column<int>(type: "int", nullable: false),
                    CorrectAnswer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AcceptedAnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Weight = table.Column<double>(type: "float", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowsAudioRecording = table.Column<bool>(type: "bit", nullable: false),
                    AudioPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningQuestions_ScreeningTests_ScreeningTestId",
                        column: x => x.ScreeningTestId,
                        principalTable: "ScreeningTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScreeningAttemptId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RiskTier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    FlaggedAreasJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ScoringVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsReferralRecommended = table.Column<bool>(type: "bit", nullable: false),
                    SkillAreasJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    RecommendedNextAction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningReports_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningReports_ScreeningAttempts_ScreeningAttemptId",
                        column: x => x.ScreeningAttemptId,
                        principalTable: "ScreeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningTestAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScreeningAttemptId = table.Column<int>(type: "int", nullable: false),
                    ScreeningTestId = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RawScore = table.Column<double>(type: "float", nullable: true),
                    PercentageScore = table.Column<double>(type: "float", nullable: true),
                    HasFlaggedSkillArea = table.Column<bool>(type: "bit", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningTestAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningTestAttempts_ScreeningAttempts_ScreeningAttemptId",
                        column: x => x.ScreeningAttemptId,
                        principalTable: "ScreeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningTestAttempts_ScreeningTests_ScreeningTestId",
                        column: x => x.ScreeningTestId,
                        principalTable: "ScreeningTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LearningActivityId = table.Column<int>(type: "int", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RelatedScreeningReportId = table.Column<int>(type: "int", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_AspNetUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_AspNetUsers_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_LearningActivities_LearningActivityId",
                        column: x => x.LearningActivityId,
                        principalTable: "LearningActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_ScreeningReports_RelatedScreeningReportId",
                        column: x => x.RelatedScreeningReportId,
                        principalTable: "ScreeningReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScreeningReportId = table.Column<int>(type: "int", nullable: false),
                    LearnerId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AssignedPsychologistId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Referrals_AspNetUsers_AssignedPsychologistId",
                        column: x => x.AssignedPsychologistId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_AspNetUsers_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_ScreeningReports_ScreeningReportId",
                        column: x => x.ScreeningReportId,
                        principalTable: "ScreeningReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScreeningTestAttemptId = table.Column<int>(type: "int", nullable: false),
                    ScreeningQuestionId = table.Column<int>(type: "int", nullable: false),
                    TextAnswer = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    SelectedAnswer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: true),
                    Score = table.Column<double>(type: "float", nullable: true),
                    ResponseTimeMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    AudioRecordingPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ProfessionalAudioNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProfessionalAudioNoteUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseDetailsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningResponses_ScreeningQuestions_ScreeningQuestionId",
                        column: x => x.ScreeningQuestionId,
                        principalTable: "ScreeningQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningResponses_ScreeningTestAttempts_ScreeningTestAttemptId",
                        column: x => x.ScreeningTestAttemptId,
                        principalTable: "ScreeningTestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActivityAssignmentId = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Score = table.Column<double>(type: "float", nullable: true),
                    Accuracy = table.Column<double>(type: "float", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityAttempts_ActivityAssignments_ActivityAssignmentId",
                        column: x => x.ActivityAssignmentId,
                        principalTable: "ActivityAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningResponseErrorCategories",
                columns: table => new
                {
                    ScreeningResponseId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningResponseErrorCategories", x => new { x.ScreeningResponseId, x.Category });
                    table.ForeignKey(
                        name: "FK_ScreeningResponseErrorCategories_ScreeningResponses_ScreeningResponseId",
                        column: x => x.ScreeningResponseId,
                        principalTable: "ScreeningResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerAudioRecordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ScreeningTestAttemptId = table.Column<int>(type: "int", nullable: true),
                    ScreeningQuestionId = table.Column<int>(type: "int", nullable: true),
                    ActivityAttemptId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsentAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsentNoticeVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StorageFileName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ProfessionalNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProfessionalNoteUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerAudioRecordings", x => x.Id);
                    table.CheckConstraint("CK_LearnerAudioRecordings_OneTarget", "(\"ActivityAttemptId\" IS NOT NULL AND \"ScreeningTestAttemptId\" IS NULL AND \"ScreeningQuestionId\" IS NULL) OR (\"ActivityAttemptId\" IS NULL AND \"ScreeningTestAttemptId\" IS NOT NULL AND \"ScreeningQuestionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_LearnerAudioRecordings_ActivityAttempts_ActivityAttemptId",
                        column: x => x.ActivityAttemptId,
                        principalTable: "ActivityAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearnerAudioRecordings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearnerAudioRecordings_ScreeningQuestions_ScreeningQuestionId",
                        column: x => x.ScreeningQuestionId,
                        principalTable: "ScreeningQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearnerAudioRecordings_ScreeningTestAttempts_ScreeningTestAttemptId",
                        column: x => x.ScreeningTestAttemptId,
                        principalTable: "ScreeningTestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LearningActivities",
                columns: new[] { "Id", "ActivityType", "AllowsAudioRecording", "Description", "Instructions", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "PhonologicalAwareness", false, "Practice blending and segmenting sound chunks in short words.", "Arrange the chunks, then blend or segment the word at your own pace.", true, "Sound Builder" },
                    { 2, "Fluency", false, "Practise naming a changing grid of familiar items at a comfortable, steady pace.", "Name each highlighted item, mark an item if you want to note a correction, and pause whenever you need.", true, "Fast Name Race" },
                    { 3, "Reading", false, "Practise reading words selected for a screening support area.", "Read each displayed word aloud and choose how the practice try went. Speech is not automatically evaluated; optional recording requires your consent.", true, "Word Reader" },
                    { 4, "Reading", false, "Practise arranging syllable chunks to build longer words.", "Look at the word, arrange its syllable chunks, then check your answer at a comfortable pace.", true, "Syllable Slicer" },
                    { 5, "Spelling", false, "Practise matching letter chunks to build a spelled word.", "Listen to pronunciation audio when available, read the clue, and arrange the chunks to build each word.", true, "Say-Write-Match" }
                });

            migrationBuilder.InsertData(
                table: "ScreeningTests",
                columns: new[] { "Id", "Description", "Instructions", "IsActive", "Name", "Slug", "SortOrder" },
                values: new object[,]
                {
                    { 1, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Word Reading", "word-reading", 1 },
                    { 2, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Decoding / Non-word Reading", "decoding-non-word-reading", 2 },
                    { 3, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Phonological Awareness", "phonological-awareness", 3 },
                    { 4, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Rapid Naming", "rapid-naming", 4 },
                    { 5, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Spelling", "spelling", 5 },
                    { 6, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Reading Fluency", "reading-fluency", 6 },
                    { 7, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Reading Comprehension", "reading-comprehension", 7 },
                    { 8, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Oral Language and Listening Comprehension", "oral-language-listening-comprehension", 8 },
                    { 9, "Optional category. Disabled pending stakeholder and professional confirmation.", "No screening items are configured yet.", false, "Memory and Broader Thinking Skills", "memory-broader-thinking-skills", 9 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_AssignedByUserId",
                table: "ActivityAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_LearnerId_Status_AssignedAtUtc",
                table: "ActivityAssignments",
                columns: new[] { "LearnerId", "Status", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_LearningActivityId",
                table: "ActivityAssignments",
                column: "LearningActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_RelatedScreeningReportId",
                table: "ActivityAssignments",
                column: "RelatedScreeningReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttempts_ActivityAssignmentId_StartedAtUtc",
                table: "ActivityAttempts",
                columns: new[] { "ActivityAssignmentId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttempts_OneInProgressPerAssignment",
                table: "ActivityAttempts",
                column: "ActivityAssignmentId",
                unique: true,
                filter: "\"CompletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppNotifications_RecipientUserId_ReadAtUtc_CreatedAtUtc",
                table: "AppNotifications",
                columns: new[] { "RecipientUserId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentDrafts_ScreeningTestId_IsDiscarded",
                table: "AssessmentDrafts",
                columns: new[] { "ScreeningTestId", "IsDiscarded" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action_EntityType_OccurredAtUtc",
                table: "AuditEvents",
                columns: new[] { "Action", "EntityType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorUserId_OccurredAtUtc",
                table: "AuditEvents",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAtUtc",
                table: "AuditEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EducationalResources_IsPublished_UpdatedAtUtc",
                table: "EducationalResources",
                columns: new[] { "IsPublished", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAudioRecordings_ActivityAttemptId",
                table: "LearnerAudioRecordings",
                column: "ActivityAttemptId",
                unique: true,
                filter: "\"Status\" = 0 AND \"ActivityAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAudioRecordings_ScreeningQuestionId",
                table: "LearnerAudioRecordings",
                column: "ScreeningQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAudioRecordings_ScreeningTestAttemptId_ScreeningQuestionId",
                table: "LearnerAudioRecordings",
                columns: new[] { "ScreeningTestAttemptId", "ScreeningQuestionId" },
                unique: true,
                filter: "\"Status\" = 0 AND \"ScreeningTestAttemptId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAudioRecordings_StorageFileName",
                table: "LearnerAudioRecordings",
                column: "StorageFileName",
                unique: true,
                filter: "[StorageFileName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerAudioRecordings_UserId_CreatedAtUtc",
                table: "LearnerAudioRecordings",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerInterests_UserId",
                table: "LearnerInterests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerInterests_UserId_InterestKey",
                table: "LearnerInterests",
                columns: new[] { "UserId", "InterestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerReminderPreferences_UserId",
                table: "LearnerReminderPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningActivities_IsActive_ActivityType",
                table: "LearningActivities",
                columns: new[] { "IsActive", "ActivityType" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeStories_LearnerId_GeneratedAtUtc",
                table: "PracticeStories",
                columns: new[] { "LearnerId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_AssignedPsychologistId_Status_CreatedAtUtc",
                table: "Referrals",
                columns: new[] { "AssignedPsychologistId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_LearnerId_CreatedAtUtc",
                table: "Referrals",
                columns: new[] { "LearnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ScreeningReportId",
                table: "Referrals",
                column: "ScreeningReportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningAttempts_CurrentTestId",
                table: "ScreeningAttempts",
                column: "CurrentTestId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningAttempts_OneInProgressPerUser",
                table: "ScreeningAttempts",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningAttempts_UserId_StartedAtUtc",
                table: "ScreeningAttempts",
                columns: new[] { "UserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningQuestions_ScreeningTestId_SortOrder",
                table: "ScreeningQuestions",
                columns: new[] { "ScreeningTestId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningReports_ScreeningAttemptId",
                table: "ScreeningReports",
                column: "ScreeningAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningReports_UserId",
                table: "ScreeningReports",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResponses_ScreeningQuestionId",
                table: "ScreeningResponses",
                column: "ScreeningQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResponses_ScreeningTestAttemptId_ScreeningQuestionId",
                table: "ScreeningResponses",
                columns: new[] { "ScreeningTestAttemptId", "ScreeningQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningTestAttempts_ScreeningAttemptId_ScreeningTestId",
                table: "ScreeningTestAttempts",
                columns: new[] { "ScreeningAttemptId", "ScreeningTestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningTestAttempts_ScreeningAttemptId_StartedAtUtc",
                table: "ScreeningTestAttempts",
                columns: new[] { "ScreeningAttemptId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningTestAttempts_ScreeningTestId",
                table: "ScreeningTestAttempts",
                column: "ScreeningTestId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningTests_Slug",
                table: "ScreeningTests",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccessibilityPreferences_UserId",
                table: "UserAccessibilityPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppNotifications");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AssessmentDrafts");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "EducationalResources");

            migrationBuilder.DropTable(
                name: "LearnerAudioRecordings");

            migrationBuilder.DropTable(
                name: "LearnerInterests");

            migrationBuilder.DropTable(
                name: "LearnerReminderPreferences");

            migrationBuilder.DropTable(
                name: "PracticeStories");

            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropTable(
                name: "ScreeningResponseErrorCategories");

            migrationBuilder.DropTable(
                name: "UserAccessibilityPreferences");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "ActivityAttempts");

            migrationBuilder.DropTable(
                name: "ScreeningResponses");

            migrationBuilder.DropTable(
                name: "ActivityAssignments");

            migrationBuilder.DropTable(
                name: "ScreeningQuestions");

            migrationBuilder.DropTable(
                name: "ScreeningTestAttempts");

            migrationBuilder.DropTable(
                name: "LearningActivities");

            migrationBuilder.DropTable(
                name: "ScreeningReports");

            migrationBuilder.DropTable(
                name: "ScreeningAttempts");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "ScreeningTests");
        }
    }
}
