-- Run in SSMS using a LOCAL copy with the approved login. Only an existing active DEV user can be changed.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'PrivateBrandsPortal_DEV'
    THROW 50001, 'This helper is restricted to PrivateBrandsPortal_DEV.', 1;
DECLARE @DomainLogin nvarchar(256) = N'<DOMAIN\username>';
DECLARE @Role nvarchar(32) = N'Manager'; -- Use ProjectManager to restore after a DEV test.
IF CHARINDEX(N'<', @DomainLogin) > 0 OR CHARINDEX(N'\', @DomainLogin) < 2
    THROW 50002, 'Specify an explicitly approved existing domain account.', 1;
IF @Role NOT IN (N'Manager', N'ProjectManager', N'SuperAdmin')
    THROW 50003, 'Supported roles: Manager, ProjectManager, first SuperAdmin.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock @Resource=N'PrivateBrandsPortal.UserAdministration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
    IF @LockResult < 0 THROW 50005, 'User administration is busy.', 1;
    IF @Role=N'SuperAdmin' AND EXISTS(SELECT 1 FROM dbo.AppUsers WHERE Role=N'SuperAdmin' AND IsActive=1)
        THROW 50006, 'An active SuperAdmin already exists. Use Administration / Users.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WITH (UPDLOCK, HOLDLOCK) WHERE DomainLogin=@DomainLogin AND IsActive=1 AND Role <> N'SuperAdmin')
        THROW 50004, 'Active non-admin user not found; no account was changed.', 1;
    UPDATE dbo.AppUsers SET Role=@Role, UpdatedAtUtc=TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') WHERE DomainLogin=@DomainLogin;
    SELECT Id, DomainLogin, Role FROM dbo.AppUsers WHERE DomainLogin=@DomainLogin;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
