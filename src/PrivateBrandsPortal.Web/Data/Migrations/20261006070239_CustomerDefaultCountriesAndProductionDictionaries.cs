using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDefaultCountriesAndProductionDictionaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultCountryId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_DefaultCountryId",
                table: "Customers",
                column: "DefaultCountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Countries_DefaultCountryId",
                table: "Customers",
                column: "DefaultCountryId",
                principalTable: "Countries",
                principalColumn: "Id");

            migrationBuilder.Sql("""
DECLARE @now datetimeoffset = SYSUTCDATETIME();
-- Normalize comparison keys only; never rewrite project snapshots.
CREATE TABLE #Names (Kind int NOT NULL, Id int NOT NULL, ParentId int NULL, Name nvarchar(200) COLLATE Latin1_General_100_CI_AS NOT NULL);
INSERT #Names SELECT 1,Id,NULL,Name FROM Countries;
INSERT #Names SELECT 2,Id,NULL,Name FROM Customers;
INSERT #Names SELECT 3,Id,NULL,Name FROM ProductCategories;
INSERT #Names SELECT 4,Id,ProductCategoryId,Name FROM ProductSubcategories;
UPDATE #Names SET Name=UPPER(TRIM(REPLACE(REPLACE(REPLACE(REPLACE(Name,NCHAR(160),N' '),NCHAR(9),N' '),NCHAR(10),N' '),NCHAR(13),N' ')));
WHILE EXISTS(SELECT 1 FROM #Names WHERE Name LIKE N'%  %') UPDATE #Names SET Name=REPLACE(Name,N'  ',N' ');
DECLARE @Countries table(Code nvarchar(2),Name nvarchar(100),Sort int,Id int NULL);
INSERT @Countries(Code,Name,Sort) VALUES (N'PL',N'Poland',10),(N'BE',N'Belgium',60),(N'FI',N'Finland',70),(N'NL',N'Netherlands',80),(N'CZ',N'The Czech Republic',90);
DECLARE @code nvarchar(2),@name nvarchar(200),@sort int,@id int,@parent int,@count int;
DECLARE countries_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Code,Name,Sort FROM @Countries;
OPEN countries_cursor; FETCH NEXT FROM countries_cursor INTO @code,@name,@sort;
WHILE @@FETCH_STATUS=0 BEGIN
 SELECT @count=COUNT(*),@id=MIN(c.Id) FROM Countries c JOIN #Names n ON n.Kind=1 AND n.Id=c.Id
 WHERE c.Code=@code OR n.Name=UPPER(@name) COLLATE Latin1_General_100_CI_AS OR (@code=N'CZ' AND n.Name IN(N'CZECH REPUBLIC',N'CZECHIA'));
 IF @count>1 THROW 51000,'Country dictionary conflict: multiple matching names/codes. Resolve before retrying.',1;
 IF @count=1 AND EXISTS(SELECT 1 FROM Countries WHERE Id=@id AND Code<>@code) THROW 51000,'Country dictionary conflict: matching name has a different ISO code.',1;
 IF @count=0 BEGIN INSERT Countries(Name,Code,IsActive,DisplayOrder,UpdatedAtUtc) VALUES(@name,@code,1,@sort,@now);SET @id=SCOPE_IDENTITY();END
 ELSE UPDATE Countries SET IsActive=1,UpdatedAtUtc=@now WHERE Id=@id AND IsActive=0;
 UPDATE @Countries SET Id=@id WHERE Code=@code;
 FETCH NEXT FROM countries_cursor INTO @code,@name,@sort;
END
CLOSE countries_cursor;DEALLOCATE countries_cursor;
DECLARE @Customers table(Name nvarchar(200),Code nvarchar(2),Sort int);
INSERT @Customers VALUES
(N'CARREFOUR',N'PL',1),(N'DOZ',N'PL',2),(N'NATURA',N'PL',3),(N'POLWELL / MILA',N'PL',4),
(N'ALBA THYMENT',N'PL',5),(N'BIEDRONKA',N'PL',6),(N'POLOMARKET',N'PL',7),(N'ALDI',N'PL',8),
(N'HEBE',N'PL',9),(N'HERBAPOL',N'PL',10),(N'LIDL',N'PL',11),(N'BETLEY',N'PL',12),
(N'GEMINI',N'PL',13),(N'ULTRAMENT',N'PL',14),(N'COLRUYT',N'BE',15),(N'TOKMANNI',N'FI',16),
(N'ORCHARD',N'NL',17),(N'DR.MAX',N'CZ',18),(N'PHARMA BOULEVARD',N'BE',19),(N'CERES PHARMA',N'BE',20),
(N'BEYOND LABELS',N'NL',21),(N'MDV / STYLEDRY',N'NL',22);
DECLARE customers_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name,Code,Sort FROM @Customers;
OPEN customers_cursor;FETCH NEXT FROM customers_cursor INTO @name,@code,@sort;
WHILE @@FETCH_STATUS=0 BEGIN
 SELECT @parent=Id FROM @Countries WHERE Code=@code;
 SELECT @count=COUNT(*),@id=MIN(Id) FROM #Names WHERE Kind=2 AND Name=@name COLLATE Latin1_General_100_CI_AS;
 IF @count>1 THROW 51000,'Customer dictionary conflict: duplicate normalized names.',1;
 IF @count=1 AND EXISTS(SELECT 1 FROM Customers WHERE Id=@id AND DefaultCountryId IS NOT NULL AND DefaultCountryId<>@parent) THROW 51000,'Customer default country conflicts with the requested mapping.',1;
 IF @count=0 INSERT Customers(Name,DefaultCountryId,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc) VALUES(@name,@parent,1,@sort,@now,@now);
 ELSE UPDATE Customers SET DefaultCountryId=COALESCE(DefaultCountryId,@parent),IsActive=1,DisplayOrder=@sort,UpdatedAtUtc=@now WHERE Id=@id;
 FETCH NEXT FROM customers_cursor INTO @name,@code,@sort;
END
CLOSE customers_cursor;DEALLOCATE customers_cursor;
DECLARE @Categories table(Name nvarchar(100),Sort int,Id int NULL);
INSERT @Categories(Name,Sort) VALUES(N'BODY CARE',1),(N'HAIR CARE',2),(N'FACE CARE',3);
DECLARE categories_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name,Sort FROM @Categories;
OPEN categories_cursor;FETCH NEXT FROM categories_cursor INTO @name,@sort;
WHILE @@FETCH_STATUS=0 BEGIN
 SELECT @count=COUNT(*),@id=MIN(Id) FROM #Names WHERE Kind=3 AND (Name=@name COLLATE Latin1_General_100_CI_AS OR (@name=N'FACE CARE' AND Name=N'FACE CREAM'));
 IF @count>1 THROW 51000,'Product category conflict: multiple canonical/legacy matches.',1;
 IF @count=0 BEGIN INSERT ProductCategories(Name,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc) VALUES(@name,1,@sort,@now,@now);SET @id=SCOPE_IDENTITY();END
 ELSE UPDATE ProductCategories SET Name=@name,IsActive=1,DisplayOrder=@sort,UpdatedAtUtc=@now WHERE Id=@id;
 UPDATE @Categories SET Id=@id WHERE Name=@name;
 FETCH NEXT FROM categories_cursor INTO @name,@sort;
END
CLOSE categories_cursor;DEALLOCATE categories_cursor;
DECLARE @Subcategories table(Category nvarchar(100),Name nvarchar(100),Sort int);
INSERT @Subcategories VALUES
(N'BODY CARE',N'LIQUID SOAP',1),(N'BODY CARE',N'SHOWER GEL',2),(N'BODY CARE',N'SHOWER GEL & SHAMPOO',3),
(N'BODY CARE',N'BATH FOAM',4),(N'BODY CARE',N'BATH SALT',5),(N'BODY CARE',N'BODY LOTION',6),
(N'BODY CARE',N'BODY BUTTER',7),(N'BODY CARE',N'HAND CREAM',8),(N'BODY CARE',N'FOOT CREAM',9),
(N'BODY CARE',N'INTIMATE HYGIENE',10),(N'BODY CARE',N'CREAMY BODY SCRUB',11),(N'BODY CARE',N'SHOWER & PEELING',12),(N'BODY CARE',N'BODY MIST',13),
(N'HAIR CARE',N'SHAMPOO',1),(N'HAIR CARE',N'HAIR CONDITIONER',2),(N'HAIR CARE',N'SHAMPOO & CONDINTIONER',3),
(N'HAIR CARE',N'HAIR MASK',4),(N'HAIR CARE',N'HAIR SPRAY',5),(N'HAIR CARE',N'LEAVE IN CONDITIONER',6),
(N'HAIR CARE',N'HAIR SERUM',7),(N'HAIR CARE',N'SCALP PEELING',8),
(N'FACE CARE',N'FACE CREAM',1),(N'FACE CARE',N'MICELLAR WATER',2),(N'FACE CARE',N'CLEANSING GEL',3),
(N'FACE CARE',N'CLEANSING FOAM',4),(N'FACE CARE',N'CLEANSING MILK',5),(N'FACE CARE',N'FACE TONIC',6);
DECLARE subcategories_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT s.Name,s.Sort,c.Id FROM @Subcategories s JOIN @Categories c ON c.Name=s.Category;
OPEN subcategories_cursor;FETCH NEXT FROM subcategories_cursor INTO @name,@sort,@parent;
WHILE @@FETCH_STATUS=0 BEGIN
 IF EXISTS(SELECT 1 FROM #Names WHERE Kind=4 AND Name=@name COLLATE Latin1_General_100_CI_AS AND ParentId<>@parent) THROW 51000,'Subcategory conflict: matching name belongs to another category. No automatic move is allowed.',1;
 SELECT @count=COUNT(*),@id=MIN(Id) FROM #Names WHERE Kind=4 AND Name=@name COLLATE Latin1_General_100_CI_AS AND ParentId=@parent;
 IF @count>1 THROW 51000,'Subcategory conflict: duplicate normalized names within category.',1;
 IF @count=0 INSERT ProductSubcategories(Name,ProductCategoryId,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc) VALUES(@name,@parent,1,@sort,@now,@now);
 ELSE UPDATE ProductSubcategories SET IsActive=1,DisplayOrder=@sort,UpdatedAtUtc=@now WHERE Id=@id;
 FETCH NEXT FROM subcategories_cursor INTO @name,@sort,@parent;
END
CLOSE subcategories_cursor;DEALLOCATE subcategories_cursor;
DROP TABLE #Names;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("THROW 51000, 'This migration contains editable business data. Restore a reviewed backup or use a forward migration; automatic rollback is disabled.', 1;");
        }
    }
}
