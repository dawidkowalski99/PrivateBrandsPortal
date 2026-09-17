-- Read-only verification after migration. Execute in SSMS against the confirmed instance.
IF DB_NAME() <> N'PrivateBrandsPortal_DEV'
    THROW 50001, 'Select PrivateBrandsPortal_DEV.', 1;

SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory;
SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') ORDER BY name;
SELECT Id, Name, Code, IsActive, DisplayOrder FROM dbo.Countries ORDER BY Id;
SELECT Id, Name, IsActive, DisplayOrder, CreatedAtUtc, UpdatedAtUtc FROM dbo.ProductTypes ORDER BY Id;
SELECT name, start_value, increment, is_cycling FROM sys.sequences WHERE name = N'ProjectNumberSequence';
SELECT name, delete_referential_action_desc FROM sys.foreign_keys ORDER BY name;
