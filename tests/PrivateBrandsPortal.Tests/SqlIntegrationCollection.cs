namespace PrivateBrandsPortal.Tests;

// These tests share one DEV database. Cleanup deletes through foreign-key subqueries;
// do not run it concurrently with another test's review transaction.
[CollectionDefinition("SQL integration", DisableParallelization = true)]
public sealed class SqlIntegrationCollection { }
