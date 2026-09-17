-- Run manually in SSMS AFTER applying InitialBusinessSchema.
-- Edit these two placeholders in a LOCAL copy. Never commit a real domain login.
-- This script only creates the first DEV administrator; it never promotes an existing user.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'PrivateBrandsPortal_DEV'
    THROW 50001, 'Select PrivateBrandsPortal_DEV before running this script.', 1;

DECLARE @DomainLogin nvarchar(256) = N'<DOMAIN\username>';
DECLARE @DisplayName nvarchar(200) = N'<Display name>';

IF CHARINDEX(N'<', @DomainLogin) > 0 OR CHARINDEX(N'<', @DisplayName) > 0
    OR CHARINDEX(N'\', @DomainLogin) < 2 OR RIGHT(@DomainLogin, 1) = N'\'
    OR LEN(LTRIM(RTRIM(@DisplayName))) = 0
    THROW 50002, 'Replace both placeholders with an explicitly approved domain account.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM dbo.AppUsers WITH (UPDLOCK, HOLDLOCK)
               WHERE DomainLogin = @DomainLogin OR Role = N'Admin')
        THROW 50003, 'User or administrator already exists. No account was changed.', 1;

    DECLARE @Now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
    INSERT INTO dbo.AppUsers
        (DomainLogin, DisplayName, Email, Department, Role, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES
        (@DomainLogin, @DisplayName, NULL, NULL, N'Admin', 1, @Now, @Now);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
