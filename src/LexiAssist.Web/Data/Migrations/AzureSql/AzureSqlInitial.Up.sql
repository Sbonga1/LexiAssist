CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [FirstName] nvarchar(max) NOT NULL,
    [LastName] nvarchar(max) NOT NULL,
    [DateOfBirth] date NULL,
    [PreferredName] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ScreeningPrivacyNoticeAcceptedAtUtc] datetime2 NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [EducationalResources] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(200) NOT NULL,
    [Summary] nvarchar(1000) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [ResourceType] nvarchar(64) NOT NULL,
    [ExternalUrl] nvarchar(2048) NULL,
    [IsPublished] bit NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_EducationalResources] PRIMARY KEY ([Id])
);

CREATE TABLE [LearningActivities] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [ActivityType] nvarchar(40) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    [Instructions] nvarchar(4000) NOT NULL,
    [IsActive] bit NOT NULL,
    [AllowsAudioRecording] bit NOT NULL,
    CONSTRAINT [PK_LearningActivities] PRIMARY KEY ([Id])
);

CREATE TABLE [ScreeningTests] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Slug] nvarchar(160) NOT NULL,
    [Description] nvarchar(2000) NOT NULL,
    [Instructions] nvarchar(4000) NOT NULL,
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_ScreeningTests] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AppNotifications] (
    [Id] int NOT NULL IDENTITY,
    [RecipientUserId] nvarchar(450) NOT NULL,
    [Type] int NOT NULL,
    [Title] nvarchar(160) NOT NULL,
    [Message] nvarchar(2000) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ReadAtUtc] datetime2 NULL,
    [RelatedUrl] nvarchar(2048) NULL,
    CONSTRAINT [PK_AppNotifications] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AppNotifications_AspNetUsers_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AuditEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [OccurredAtUtc] datetime2 NOT NULL,
    [ActorUserId] nvarchar(450) NULL,
    [Action] nvarchar(120) NOT NULL,
    [EntityType] nvarchar(120) NOT NULL,
    [EntityId] nvarchar(200) NULL,
    [Succeeded] bit NOT NULL,
    [DetailsJson] nvarchar(4000) NULL,
    CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AuditEvents_AspNetUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE SET NULL
);

CREATE TABLE [LearnerInterests] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [InterestKey] nvarchar(100) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_LearnerInterests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LearnerInterests_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [LearnerReminderPreferences] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [RemindersEnabled] bit NOT NULL,
    [PreferredReminderTime] time NOT NULL,
    [DailyPracticeTargetMinutes] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_LearnerReminderPreferences] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LearnerReminderPreferences_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [PracticeStories] (
    [Id] int NOT NULL IDENTITY,
    [LearnerId] nvarchar(450) NOT NULL,
    [Theme] nvarchar(120) NOT NULL,
    [TargetPhoneticPattern] nvarchar(120) NOT NULL,
    [TargetWordsJson] nvarchar(2000) NOT NULL,
    [DifficultyLevel] int NOT NULL,
    [StoryText] nvarchar(2000) NOT NULL,
    [GenerationMethod] nvarchar(80) NOT NULL,
    [GeneratedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_PracticeStories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PracticeStories_AspNetUsers_LearnerId] FOREIGN KEY ([LearnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [UserAccessibilityPreferences] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [BackgroundTheme] nvarchar(32) NOT NULL,
    [FontScale] float NOT NULL,
    [IncreasedLineSpacing] bit NOT NULL,
    [ReduceMotion] bit NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_UserAccessibilityPreferences] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserAccessibilityPreferences_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AssessmentDrafts] (
    [Id] uniqueidentifier NOT NULL,
    [ScreeningTestId] int NOT NULL,
    [IsCsvImport] bit NOT NULL,
    [ContentJson] nvarchar(max) NOT NULL,
    [IsDiscarded] bit NOT NULL,
    [Version] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_AssessmentDrafts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AssessmentDrafts_ScreeningTests_ScreeningTestId] FOREIGN KEY ([ScreeningTestId]) REFERENCES [ScreeningTests] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [ScreeningAttempts] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [StartedAtUtc] datetime2 NOT NULL,
    [CompletedAtUtc] datetime2 NULL,
    [Status] int NOT NULL,
    [OverallScore] float NULL,
    [RiskTier] nvarchar(64) NULL,
    [RiskCalculationVersion] nvarchar(64) NULL,
    [RiskCalculatedAtUtc] datetime2 NULL,
    [CurrentTestId] int NULL,
    CONSTRAINT [PK_ScreeningAttempts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ScreeningAttempts_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ScreeningAttempts_ScreeningTests_CurrentTestId] FOREIGN KEY ([CurrentTestId]) REFERENCES [ScreeningTests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ScreeningQuestions] (
    [Id] int NOT NULL IDENTITY,
    [ScreeningTestId] int NOT NULL,
    [Prompt] nvarchar(2000) NOT NULL,
    [QuestionType] int NOT NULL,
    [CorrectAnswer] nvarchar(2000) NULL,
    [AcceptedAnswersJson] nvarchar(max) NULL,
    [Weight] float NOT NULL,
    [SortOrder] int NOT NULL,
    [IsActive] bit NOT NULL,
    [AllowsAudioRecording] bit NOT NULL,
    [AudioPath] nvarchar(1024) NULL,
    [ImagePath] nvarchar(1024) NULL,
    [MetadataJson] nvarchar(max) NULL,
    CONSTRAINT [PK_ScreeningQuestions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ScreeningQuestions_ScreeningTests_ScreeningTestId] FOREIGN KEY ([ScreeningTestId]) REFERENCES [ScreeningTests] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [ScreeningReports] (
    [Id] int NOT NULL IDENTITY,
    [ScreeningAttemptId] int NOT NULL,
    [UserId] nvarchar(450) NOT NULL,
    [GeneratedAtUtc] datetime2 NOT NULL,
    [RiskTier] nvarchar(64) NULL,
    [Summary] nvarchar(4000) NOT NULL,
    [FlaggedAreasJson] nvarchar(max) NOT NULL,
    [ScoringVersion] nvarchar(64) NOT NULL,
    [IsReferralRecommended] bit NOT NULL,
    [SkillAreasJson] nvarchar(max) NOT NULL,
    [RecommendedNextAction] nvarchar(2000) NOT NULL,
    CONSTRAINT [PK_ScreeningReports] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ScreeningReports_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ScreeningReports_ScreeningAttempts_ScreeningAttemptId] FOREIGN KEY ([ScreeningAttemptId]) REFERENCES [ScreeningAttempts] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ScreeningTestAttempts] (
    [Id] int NOT NULL IDENTITY,
    [ScreeningAttemptId] int NOT NULL,
    [ScreeningTestId] int NOT NULL,
    [StartedAtUtc] datetime2 NOT NULL,
    [CompletedAtUtc] datetime2 NULL,
    [RawScore] float NULL,
    [PercentageScore] float NULL,
    [HasFlaggedSkillArea] bit NULL,
    [DurationMilliseconds] bigint NULL,
    CONSTRAINT [PK_ScreeningTestAttempts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ScreeningTestAttempts_ScreeningAttempts_ScreeningAttemptId] FOREIGN KEY ([ScreeningAttemptId]) REFERENCES [ScreeningAttempts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ScreeningTestAttempts_ScreeningTests_ScreeningTestId] FOREIGN KEY ([ScreeningTestId]) REFERENCES [ScreeningTests] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ActivityAssignments] (
    [Id] int NOT NULL IDENTITY,
    [LearnerId] nvarchar(450) NOT NULL,
    [LearningActivityId] int NOT NULL,
    [AssignedByUserId] nvarchar(450) NULL,
    [RelatedScreeningReportId] int NULL,
    [AssignedAtUtc] datetime2 NOT NULL,
    [CompletedAtUtc] datetime2 NULL,
    [Status] nvarchar(32) NOT NULL,
    CONSTRAINT [PK_ActivityAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ActivityAssignments_AspNetUsers_AssignedByUserId] FOREIGN KEY ([AssignedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ActivityAssignments_AspNetUsers_LearnerId] FOREIGN KEY ([LearnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ActivityAssignments_LearningActivities_LearningActivityId] FOREIGN KEY ([LearningActivityId]) REFERENCES [LearningActivities] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ActivityAssignments_ScreeningReports_RelatedScreeningReportId] FOREIGN KEY ([RelatedScreeningReportId]) REFERENCES [ScreeningReports] ([Id]) ON DELETE SET NULL
);

CREATE TABLE [Referrals] (
    [Id] int NOT NULL IDENTITY,
    [ScreeningReportId] int NOT NULL,
    [LearnerId] nvarchar(450) NOT NULL,
    [AssignedPsychologistId] nvarchar(450) NULL,
    [Status] nvarchar(32) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ReviewedAtUtc] datetime2 NULL,
    [Notes] nvarchar(4000) NULL,
    CONSTRAINT [PK_Referrals] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Referrals_AspNetUsers_AssignedPsychologistId] FOREIGN KEY ([AssignedPsychologistId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Referrals_AspNetUsers_LearnerId] FOREIGN KEY ([LearnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Referrals_ScreeningReports_ScreeningReportId] FOREIGN KEY ([ScreeningReportId]) REFERENCES [ScreeningReports] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ScreeningResponses] (
    [Id] int NOT NULL IDENTITY,
    [ScreeningTestAttemptId] int NOT NULL,
    [ScreeningQuestionId] int NOT NULL,
    [TextAnswer] nvarchar(max) NULL,
    [SelectedAnswer] nvarchar(2000) NULL,
    [IsCorrect] bit NULL,
    [Score] float NULL,
    [ResponseTimeMilliseconds] bigint NULL,
    [AudioRecordingPath] nvarchar(1024) NULL,
    [ProfessionalAudioNote] nvarchar(500) NULL,
    [ProfessionalAudioNoteUpdatedAtUtc] datetime2 NULL,
    [ResponseDetailsJson] nvarchar(max) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_ScreeningResponses] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ScreeningResponses_ScreeningQuestions_ScreeningQuestionId] FOREIGN KEY ([ScreeningQuestionId]) REFERENCES [ScreeningQuestions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ScreeningResponses_ScreeningTestAttempts_ScreeningTestAttemptId] FOREIGN KEY ([ScreeningTestAttemptId]) REFERENCES [ScreeningTestAttempts] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ActivityAttempts] (
    [Id] int NOT NULL IDENTITY,
    [ActivityAssignmentId] int NOT NULL,
    [StartedAtUtc] datetime2 NOT NULL,
    [CompletedAtUtc] datetime2 NULL,
    [Score] float NULL,
    [Accuracy] float NULL,
    [DurationMilliseconds] bigint NULL,
    [MetadataJson] nvarchar(max) NULL,
    CONSTRAINT [PK_ActivityAttempts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ActivityAttempts_ActivityAssignments_ActivityAssignmentId] FOREIGN KEY ([ActivityAssignmentId]) REFERENCES [ActivityAssignments] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ScreeningResponseErrorCategories] (
    [ScreeningResponseId] int NOT NULL,
    [Category] nvarchar(40) NOT NULL,
    CONSTRAINT [PK_ScreeningResponseErrorCategories] PRIMARY KEY ([ScreeningResponseId], [Category]),
    CONSTRAINT [FK_ScreeningResponseErrorCategories_ScreeningResponses_ScreeningResponseId] FOREIGN KEY ([ScreeningResponseId]) REFERENCES [ScreeningResponses] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [LearnerAudioRecordings] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ScreeningTestAttemptId] int NULL,
    [ScreeningQuestionId] int NULL,
    [ActivityAttemptId] int NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ConsentAcceptedAtUtc] datetime2 NOT NULL,
    [ConsentNoticeVersion] nvarchar(40) NOT NULL,
    [StartedAtUtc] datetime2 NOT NULL,
    [UploadedAtUtc] datetime2 NULL,
    [DurationMilliseconds] bigint NULL,
    [FileSizeBytes] bigint NULL,
    [ContentType] nvarchar(64) NULL,
    [StorageFileName] nvarchar(80) NULL,
    [ProfessionalNote] nvarchar(500) NULL,
    [ProfessionalNoteUpdatedAtUtc] datetime2 NULL,
    [Status] int NOT NULL,
    CONSTRAINT [PK_LearnerAudioRecordings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_LearnerAudioRecordings_OneTarget] CHECK (([ActivityAttemptId] IS NOT NULL AND [ScreeningTestAttemptId] IS NULL AND [ScreeningQuestionId] IS NULL) OR ([ActivityAttemptId] IS NULL AND [ScreeningTestAttemptId] IS NOT NULL AND [ScreeningQuestionId] IS NOT NULL)),
    CONSTRAINT [FK_LearnerAudioRecordings_ActivityAttempts_ActivityAttemptId] FOREIGN KEY ([ActivityAttemptId]) REFERENCES [ActivityAttempts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LearnerAudioRecordings_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LearnerAudioRecordings_ScreeningQuestions_ScreeningQuestionId] FOREIGN KEY ([ScreeningQuestionId]) REFERENCES [ScreeningQuestions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LearnerAudioRecordings_ScreeningTestAttempts_ScreeningTestAttemptId] FOREIGN KEY ([ScreeningTestAttemptId]) REFERENCES [ScreeningTestAttempts] ([Id]) ON DELETE NO ACTION
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ActivityType', N'AllowsAudioRecording', N'Description', N'Instructions', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[LearningActivities]'))
    SET IDENTITY_INSERT [LearningActivities] ON;
INSERT INTO [LearningActivities] ([Id], [ActivityType], [AllowsAudioRecording], [Description], [Instructions], [IsActive], [Name])
VALUES (1, N'PhonologicalAwareness', CAST(0 AS bit), N'Practice blending and segmenting sound chunks in short words.', N'Arrange the chunks, then blend or segment the word at your own pace.', CAST(1 AS bit), N'Sound Builder'),
(2, N'Fluency', CAST(0 AS bit), N'Practise naming a changing grid of familiar items at a comfortable, steady pace.', N'Name each highlighted item, mark an item if you want to note a correction, and pause whenever you need.', CAST(1 AS bit), N'Fast Name Race'),
(3, N'Reading', CAST(0 AS bit), N'Practise reading words selected for a screening support area.', N'Read each displayed word aloud and choose how the practice try went. Speech is not automatically evaluated; optional recording requires your consent.', CAST(1 AS bit), N'Word Reader'),
(4, N'Reading', CAST(0 AS bit), N'Practise arranging syllable chunks to build longer words.', N'Look at the word, arrange its syllable chunks, then check your answer at a comfortable pace.', CAST(1 AS bit), N'Syllable Slicer'),
(5, N'Spelling', CAST(0 AS bit), N'Practise matching letter chunks to build a spelled word.', N'Listen to pronunciation audio when available, read the clue, and arrange the chunks to build each word.', CAST(1 AS bit), N'Say-Write-Match');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ActivityType', N'AllowsAudioRecording', N'Description', N'Instructions', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[LearningActivities]'))
    SET IDENTITY_INSERT [LearningActivities] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'Instructions', N'IsActive', N'Name', N'Slug', N'SortOrder') AND [object_id] = OBJECT_ID(N'[ScreeningTests]'))
    SET IDENTITY_INSERT [ScreeningTests] ON;
INSERT INTO [ScreeningTests] ([Id], [Description], [Instructions], [IsActive], [Name], [Slug], [SortOrder])
VALUES (1, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Word Reading', N'word-reading', 1),
(2, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Decoding / Non-word Reading', N'decoding-non-word-reading', 2),
(3, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Phonological Awareness', N'phonological-awareness', 3),
(4, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Rapid Naming', N'rapid-naming', 4),
(5, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Spelling', N'spelling', 5),
(6, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Reading Fluency', N'reading-fluency', 6),
(7, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Reading Comprehension', N'reading-comprehension', 7),
(8, N'Category for future educational screening and support content.', N'No screening items are configured yet.', CAST(1 AS bit), N'Oral Language and Listening Comprehension', N'oral-language-listening-comprehension', 8),
(9, N'Optional category. Disabled pending stakeholder and professional confirmation.', N'No screening items are configured yet.', CAST(0 AS bit), N'Memory and Broader Thinking Skills', N'memory-broader-thinking-skills', 9);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Description', N'Instructions', N'IsActive', N'Name', N'Slug', N'SortOrder') AND [object_id] = OBJECT_ID(N'[ScreeningTests]'))
    SET IDENTITY_INSERT [ScreeningTests] OFF;

CREATE INDEX [IX_ActivityAssignments_AssignedByUserId] ON [ActivityAssignments] ([AssignedByUserId]);

CREATE INDEX [IX_ActivityAssignments_LearnerId_Status_AssignedAtUtc] ON [ActivityAssignments] ([LearnerId], [Status], [AssignedAtUtc]);

CREATE INDEX [IX_ActivityAssignments_LearningActivityId] ON [ActivityAssignments] ([LearningActivityId]);

CREATE INDEX [IX_ActivityAssignments_RelatedScreeningReportId] ON [ActivityAssignments] ([RelatedScreeningReportId]);

CREATE INDEX [IX_ActivityAttempts_ActivityAssignmentId_StartedAtUtc] ON [ActivityAttempts] ([ActivityAssignmentId], [StartedAtUtc]);

CREATE UNIQUE INDEX [IX_ActivityAttempts_OneInProgressPerAssignment] ON [ActivityAttempts] ([ActivityAssignmentId]) WHERE [CompletedAtUtc] IS NULL;

CREATE INDEX [IX_AppNotifications_RecipientUserId_ReadAtUtc_CreatedAtUtc] ON [AppNotifications] ([RecipientUserId], [ReadAtUtc], [CreatedAtUtc]);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_AssessmentDrafts_ScreeningTestId_IsDiscarded] ON [AssessmentDrafts] ([ScreeningTestId], [IsDiscarded]);

CREATE INDEX [IX_AuditEvents_Action_EntityType_OccurredAtUtc] ON [AuditEvents] ([Action], [EntityType], [OccurredAtUtc]);

CREATE INDEX [IX_AuditEvents_ActorUserId_OccurredAtUtc] ON [AuditEvents] ([ActorUserId], [OccurredAtUtc]);

CREATE INDEX [IX_AuditEvents_OccurredAtUtc] ON [AuditEvents] ([OccurredAtUtc]);

CREATE INDEX [IX_EducationalResources_IsPublished_UpdatedAtUtc] ON [EducationalResources] ([IsPublished], [UpdatedAtUtc]);

CREATE UNIQUE INDEX [IX_LearnerAudioRecordings_ActivityAttemptId] ON [LearnerAudioRecordings] ([ActivityAttemptId]) WHERE [Status] = 0 AND [ActivityAttemptId] IS NOT NULL;

CREATE INDEX [IX_LearnerAudioRecordings_ScreeningQuestionId] ON [LearnerAudioRecordings] ([ScreeningQuestionId]);

CREATE UNIQUE INDEX [IX_LearnerAudioRecordings_ScreeningTestAttemptId_ScreeningQuestionId] ON [LearnerAudioRecordings] ([ScreeningTestAttemptId], [ScreeningQuestionId]) WHERE [Status] = 0 AND [ScreeningTestAttemptId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_LearnerAudioRecordings_StorageFileName] ON [LearnerAudioRecordings] ([StorageFileName]) WHERE [StorageFileName] IS NOT NULL;

CREATE INDEX [IX_LearnerAudioRecordings_UserId_CreatedAtUtc] ON [LearnerAudioRecordings] ([UserId], [CreatedAtUtc]);

CREATE INDEX [IX_LearnerInterests_UserId] ON [LearnerInterests] ([UserId]);

CREATE UNIQUE INDEX [IX_LearnerInterests_UserId_InterestKey] ON [LearnerInterests] ([UserId], [InterestKey]);

CREATE UNIQUE INDEX [IX_LearnerReminderPreferences_UserId] ON [LearnerReminderPreferences] ([UserId]);

CREATE INDEX [IX_LearningActivities_IsActive_ActivityType] ON [LearningActivities] ([IsActive], [ActivityType]);

CREATE INDEX [IX_PracticeStories_LearnerId_GeneratedAtUtc] ON [PracticeStories] ([LearnerId], [GeneratedAtUtc]);

CREATE INDEX [IX_Referrals_AssignedPsychologistId_Status_CreatedAtUtc] ON [Referrals] ([AssignedPsychologistId], [Status], [CreatedAtUtc]);

CREATE INDEX [IX_Referrals_LearnerId_CreatedAtUtc] ON [Referrals] ([LearnerId], [CreatedAtUtc]);

CREATE UNIQUE INDEX [IX_Referrals_ScreeningReportId] ON [Referrals] ([ScreeningReportId]);

CREATE INDEX [IX_ScreeningAttempts_CurrentTestId] ON [ScreeningAttempts] ([CurrentTestId]);

CREATE UNIQUE INDEX [IX_ScreeningAttempts_OneInProgressPerUser] ON [ScreeningAttempts] ([UserId]) WHERE [Status] = 1;

CREATE INDEX [IX_ScreeningAttempts_UserId_StartedAtUtc] ON [ScreeningAttempts] ([UserId], [StartedAtUtc]);

CREATE INDEX [IX_ScreeningQuestions_ScreeningTestId_SortOrder] ON [ScreeningQuestions] ([ScreeningTestId], [SortOrder]);

CREATE UNIQUE INDEX [IX_ScreeningReports_ScreeningAttemptId] ON [ScreeningReports] ([ScreeningAttemptId]);

CREATE INDEX [IX_ScreeningReports_UserId] ON [ScreeningReports] ([UserId]);

CREATE INDEX [IX_ScreeningResponses_ScreeningQuestionId] ON [ScreeningResponses] ([ScreeningQuestionId]);

CREATE UNIQUE INDEX [IX_ScreeningResponses_ScreeningTestAttemptId_ScreeningQuestionId] ON [ScreeningResponses] ([ScreeningTestAttemptId], [ScreeningQuestionId]);

CREATE UNIQUE INDEX [IX_ScreeningTestAttempts_ScreeningAttemptId_ScreeningTestId] ON [ScreeningTestAttempts] ([ScreeningAttemptId], [ScreeningTestId]);

CREATE INDEX [IX_ScreeningTestAttempts_ScreeningAttemptId_StartedAtUtc] ON [ScreeningTestAttempts] ([ScreeningAttemptId], [StartedAtUtc]);

CREATE INDEX [IX_ScreeningTestAttempts_ScreeningTestId] ON [ScreeningTestAttempts] ([ScreeningTestId]);

CREATE UNIQUE INDEX [IX_ScreeningTests_Slug] ON [ScreeningTests] ([Slug]);

CREATE UNIQUE INDEX [IX_UserAccessibilityPreferences_UserId] ON [UserAccessibilityPreferences] ([UserId]);
