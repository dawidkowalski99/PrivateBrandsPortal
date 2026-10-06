using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PrivateBrandsPortal.Web.Data.Migrations;
namespace PrivateBrandsPortal.Tests;

[Collection("SQL integration")]
public sealed class ProductionDictionaryMigrationTests
{
    private static readonly string[] PolishCustomers=["CARREFOUR","DOZ","NATURA","POLWELL / MILA","ALBA THYMENT","BIEDRONKA","POLOMARKET","ALDI","HEBE","HERBAPOL","LIDL","BETLEY","GEMINI","ULTRAMENT"];
    private static readonly (string Name,string Code)[] OtherCustomers=[("COLRUYT","BE"),("TOKMANNI","FI"),("ORCHARD","NL"),("DR.MAX","CZ"),("PHARMA BOULEVARD","BE"),("CERES PHARMA","BE"),("BEYOND LABELS","NL"),("MDV / STYLEDRY","NL")];
    private static readonly (string Category,string[] Names)[] Products=[
        ("BODY CARE",["LIQUID SOAP","SHOWER GEL","SHOWER GEL & SHAMPOO","BATH FOAM","BATH SALT","BODY LOTION","BODY BUTTER","HAND CREAM","FOOT CREAM","INTIMATE HYGIENE","CREAMY BODY SCRUB","SHOWER & PEELING","BODY MIST"]),
        ("HAIR CARE",["SHAMPOO","HAIR CONDITIONER","SHAMPOO & CONDINTIONER","HAIR MASK","HAIR SPRAY","LEAVE IN CONDITIONER","HAIR SERUM","SCALP PEELING"]),
        ("FACE CARE",["FACE CREAM","MICELLAR WATER","CLEANSING GEL","CLEANSING FOAM","CLEANSING MILK","FACE TONIC"])];
    private static string Normalize(string name)=>string.Join(' ',name.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    // Executes the real migration data SQL against connection-local SQL Server temp tables.
    // No CREATE DATABASE permission and no destructive changes to DEV are needed.
    private sealed class Sandbox(string connection) : IAsyncDisposable
    {
        private readonly SqlConnection sql=new(connection);
        public async Task InitializeAsync()
        {
            if(new SqlConnectionStringBuilder(connection).InitialCatalog!="PrivateBrandsPortal_DEV")throw new InvalidOperationException("DEV required.");
            await sql.OpenAsync();
            await Execute("""
                SELECT * INTO #Countries FROM dbo.Countries WHERE 1=0;
                SELECT * INTO #Customers FROM dbo.Customers WHERE 1=0;
                SELECT * INTO #ProductCategories FROM dbo.ProductCategories WHERE 1=0;
                SELECT * INTO #ProductSubcategories FROM dbo.ProductSubcategories WHERE 1=0;
                INSERT #Countries(Name,Code,IsActive,DisplayOrder,UpdatedAtUtc) VALUES
                (N'Poland',N'PL',1,10,SYSUTCDATETIME()),(N'Germany',N'DE',1,20,SYSUTCDATETIME()),
                (N'France',N'FR',1,30,SYSUTCDATETIME()),(N'Sweden',N'SE',1,40,SYSUTCDATETIME()),(N'United Kingdom',N'GB',1,50,SYSUTCDATETIME());
                INSERT #ProductCategories(Name,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc) VALUES
                (N'Hair Care',1,10,SYSUTCDATETIME(),SYSUTCDATETIME()),(N'Body Care',1,20,SYSUTCDATETIME(),SYSUTCDATETIME()),(N'Face Cream',1,30,SYSUTCDATETIME(),SYSUTCDATETIME());
                """);
        }
        public async Task Execute(string command){using var cmd=new SqlCommand(command,sql);await cmd.ExecuteNonQueryAsync();}
        public async Task<DataTable> Query(string command){using var cmd=new SqlCommand(command,sql);using var reader=await cmd.ExecuteReaderAsync();var table=new DataTable();table.Load(reader);return table;}
        public Task Apply()
        {
            var dataSql=new CustomerDefaultCountriesAndProductionDictionaries().UpOperations.OfType<SqlOperation>().Single().Sql;
            foreach(var table in new[]{"Countries","Customers","ProductCategories","ProductSubcategories"})
                dataSql=Regex.Replace(dataSql,@"(?<![@#\w])"+table+@"(?!\w)","#"+table);
            return Execute(dataSql);
        }
        public async ValueTask DisposeAsync()=>await sql.DisposeAsync();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Data_migration_populates_business_data_and_reuses_normalized_existing_entries(bool existing)
    {
        await using var dev=new ProjectSqlTests.Scope();await using var sandbox=new Sandbox(dev.Connection);await sandbox.InitializeAsync();
        int? customerId=null,subcategoryId=null;
        var faceId=(int)(await sandbox.Query("SELECT Id FROM #ProductCategories WHERE Name=N'Face Cream'")).Rows[0][0];
        if(existing)
        {
            await sandbox.Execute("INSERT #Customers(Name,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc) VALUES(NCHAR(160)+N'carrefour'+NCHAR(160),1,99,SYSUTCDATETIME(),SYSUTCDATETIME()); INSERT #ProductSubcategories(Name,ProductCategoryId,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc) SELECT N' Shampoo'+NCHAR(160),Id,1,99,SYSUTCDATETIME(),SYSUTCDATETIME() FROM #ProductCategories WHERE Name=N'Hair Care';");
            customerId=(int)(await sandbox.Query("SELECT Id FROM #Customers")).Rows[0][0];
            subcategoryId=(int)(await sandbox.Query("SELECT Id FROM #ProductSubcategories")).Rows[0][0];
        }
        await sandbox.Apply();await sandbox.Apply(); // Re-execution is duplicate-safe; startup never executes it.
        var customers=(await sandbox.Query("SELECT c.Id,c.Name,c.IsActive,c.DisplayOrder,co.Code FROM #Customers c JOIN #Countries co ON co.Id=c.DefaultCountryId ORDER BY c.DisplayOrder")).Rows.Cast<DataRow>().ToArray();
        var expected=PolishCustomers.Select(x=>(Name:x,Code:"PL")).Concat(OtherCustomers).ToArray();
        Assert.Equal(22,customers.Length);Assert.Equal(22,customers.Select(x=>Normalize((string)x["Name"])).Distinct().Count());
        for(var i=0;i<expected.Length;i++){var actual=customers[i];Assert.Equal(expected[i].Name,Normalize((string)actual["Name"]));Assert.Equal(expected[i].Code,actual["Code"]);Assert.True((bool)actual["IsActive"]);Assert.Equal(i+1,actual["DisplayOrder"]);}
        var categories=(await sandbox.Query("SELECT * FROM #ProductCategories ORDER BY DisplayOrder")).Rows.Cast<DataRow>().ToArray();Assert.Equal(3,categories.Length);
        for(var i=0;i<Products.Length;i++)
        {
            Assert.Equal(Products[i].Category,categories[i]["Name"]);Assert.True((bool)categories[i]["IsActive"]);Assert.Equal(i+1,categories[i]["DisplayOrder"]);
            var rows=(await sandbox.Query("SELECT * FROM #ProductSubcategories ORDER BY DisplayOrder")).Rows.Cast<DataRow>().Where(x=>(int)x["ProductCategoryId"]==(int)categories[i]["Id"]).ToArray();
            Assert.Equal(Products[i].Names,rows.Select(x=>Normalize((string)x["Name"])));Assert.All(rows,x=>Assert.True((bool)x["IsActive"]));
            Assert.Equal(Enumerable.Range(1,Products[i].Names.Length),rows.Select(x=>(int)x["DisplayOrder"]));
        }
        Assert.Equal(9,(await sandbox.Query("SELECT * FROM #Countries")).Rows.Count);Assert.Equal(faceId,categories[2]["Id"]);
        if(existing){Assert.Equal(customerId,customers[0]["Id"]);Assert.Contains((await sandbox.Query("SELECT * FROM #ProductSubcategories")).Rows.Cast<DataRow>(),x=>(int)x["Id"]==subcategoryId && Normalize((string)x["Name"])=="SHAMPOO");}
    }
    [Fact]
    public async Task Data_migration_reports_wrong_category_without_moving_existing_subcategory()
    {
        await using var dev=new ProjectSqlTests.Scope();await using var sandbox=new Sandbox(dev.Connection);await sandbox.InitializeAsync();
        await sandbox.Execute("INSERT #ProductSubcategories(Name,ProductCategoryId,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc) SELECT N'SHAMPOO',Id,1,1,SYSUTCDATETIME(),SYSUTCDATETIME() FROM #ProductCategories WHERE Name=N'Body Care';");
        var error=await Assert.ThrowsAsync<SqlException>(()=>sandbox.Apply());Assert.Contains("Subcategory conflict",error.Message);
        var row=(await sandbox.Query("SELECT c.Name FROM #ProductSubcategories s JOIN #ProductCategories c ON c.Id=s.ProductCategoryId WHERE s.Name=N'SHAMPOO'")).Rows[0];
        Assert.Equal("BODY CARE",row[0]);
    }
}
