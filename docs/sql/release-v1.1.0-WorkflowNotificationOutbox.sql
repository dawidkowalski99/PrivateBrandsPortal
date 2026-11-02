:On Error exit
-- PRIVATEBRANDSPORTAL v1.1.0 — GENERATED FOR LATER MANUAL EXECUTION ONLY.
-- SQLCMD mode REQUIRED (SSMS SQLCMD Mode or sqlcmd -b). Never ignore errors/continue batches.
-- Source commit: 0532655f6b7ba3c59574f3541a1ce95daeebcba2
-- EF idempotent interval: ProductCustomerRejection -> WorkflowNotificationOutbox only.
-- Stop AppPool and verify recoverable DB/application backups before running.
-- No execution was performed during release preparation.
USE [PrivateBrandsPortal];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'PrivateBrandsPortal' THROW 51000, 'Wrong database. Stop release.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THROW 51001, 'Migration history missing. Stop release.', 1;
SELECT DB_NAME() AS DatabaseName, @@SERVERNAME AS ServerName;
SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
DECLARE @Expected TABLE (MigrationId nvarchar(150) PRIMARY KEY);
INSERT INTO @Expected VALUES
(N'20260917070205_InitialBusinessSchema'),
(N'20260919144934_ManagerApprovalWorkflow'),
(N'20260930102447_ReportingAndCommercialWorkflow'),
(N'20261001083808_UserPermissions'),
(N'20261005063153_DashboardAndProjectReassignment'),
(N'20261005131116_CustomerAndSubcategoryDictionaries'),
(N'20261006070239_CustomerDefaultCountriesAndProductionDictionaries'),
(N'20261006130702_FormulaAttachmentsAndManagerOwnedProjects'),
(N'20261007112812_ImplementationApprovalAndProjectClosure'),
(N'20261007180815_ProductCustomerRejection');
IF EXISTS (SELECT MigrationId FROM @Expected EXCEPT SELECT MigrationId FROM dbo.__EFMigrationsHistory)
    THROW 51002, 'Missing prerequisite migration(s). This release cannot upgrade this database.', 1;
IF EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory h WHERE NOT EXISTS
    (SELECT 1 FROM @Expected e WHERE e.MigrationId=h.MigrationId)
    AND h.MigrationId<>N'20261030144145_WorkflowNotificationOutbox')
    THROW 51003, 'Unexpected/newer migration history. Stop and reassess release.', 1;
GO
BEGIN TRANSACTION;
DECLARE @LockResult int;
EXEC @LockResult=sys.sp_getapplock @Resource=N'PrivateBrandsPortal.ReleaseMigration',
    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
IF @LockResult < 0 THROW 51004, 'Could not acquire migration lock.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261030144145_WorkflowNotificationOutbox')
   AND (OBJECT_ID(N'dbo.NotificationOutbox',N'U') IS NOT NULL OR COL_LENGTH(N'dbo.AppUsers',N'NotificationApproverId') IS NOT NULL)
    THROW 51005, 'Outbox schema exists without migration history. Stop; do not guess or overwrite.', 1;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030144145_WorkflowNotificationOutbox'
)
BEGIN
    ALTER TABLE [dbo].[AppUsers] ADD [NotificationApproverId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030144145_WorkflowNotificationOutbox'
)
BEGIN
    CREATE TABLE [dbo].[NotificationOutbox] (
        [Id] bigint NOT NULL IDENTITY,
        [EventKey] nvarchar(64) NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [ProjectId] int NOT NULL,
        [ProductId] int NULL,
        [ActorUserId] int NOT NULL,
        [TargetUserId] int NOT NULL,
        [Audience] nvarchar(30) NOT NULL,
        [RecipientUserId] int NULL,
        [RecipientEmail] nvarchar(256) NULL,
        [Subject] nvarchar(400) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [RelativePath] nvarchar(200) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [Attempts] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [NextAttemptAtUtc] datetimeoffset NOT NULL,
        [LastAttemptAtUtc] datetimeoffset NULL,
        [SentAtUtc] datetimeoffset NULL,
        [ProcessedAtUtc] datetimeoffset NULL,
        [LastError] nvarchar(500) NULL,
        CONSTRAINT [PK_NotificationOutbox] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030144145_WorkflowNotificationOutbox'
)
BEGIN
    CREATE INDEX [IX_AppUsers_NotificationApproverId] ON [dbo].[AppUsers] ([NotificationApproverId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030144145_WorkflowNotificationOutbox'
)
BEGIN
    CREATE UNIQUE INDEX [IX_NotificationOutbox_EventKey] ON [dbo].[NotificationOutbox] ([EventKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030144145_WorkflowNotificationOutbox'
)
BEGIN
    CREATE INDEX [IX_NotificationOutbox_Status_NextAttemptAtUtc_Id] ON [dbo].[NotificationOutbox] ([Status], [NextAttemptAtUtc], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030144145_WorkflowNotificationOutbox'
)
BEGIN
    ALTER TABLE [dbo].[AppUsers] ADD CONSTRAINT [FK_AppUsers_AppUsers_NotificationApproverId] FOREIGN KEY ([NotificationApproverId]) REFERENCES [dbo].[AppUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030144145_WorkflowNotificationOutbox'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030144145_WorkflowNotificationOutbox', N'10.0.12');
END;

IF OBJECT_ID(N'dbo.NotificationOutbox', N'U') IS NULL
   OR COL_LENGTH(N'dbo.AppUsers',N'NotificationApproverId') IS NULL
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.NotificationOutbox') AND name=N'IX_NotificationOutbox_EventKey' AND is_unique=1)
   OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AppUsers_AppUsers_NotificationApproverId' AND delete_referential_action=0)
    THROW 51006, 'Post-migration schema verification failed.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261030144145_WorkflowNotificationOutbox')
    THROW 51007, 'Post-migration history verification failed.', 1;
COMMIT;
GO


SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT COUNT_BIG(*) AS NotificationOutboxRows FROM dbo.NotificationOutbox;
PRINT N'WorkflowNotificationOutbox migration present and schema checks passed. Worker must remain disabled.';
GO