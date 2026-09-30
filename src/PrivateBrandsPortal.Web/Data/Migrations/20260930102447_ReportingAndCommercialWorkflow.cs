using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportingAndCommercialWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectProducts_RejectionComment",
                table: "ProjectProducts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductReviews_RejectionComment",
                table: "ProductReviews");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAtUtc",
                table: "Projects",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ProductTypeId",
                table: "ProjectProducts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "CommercialStatus",
                table: "ProjectProducts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductCategoryId",
                table: "ProjectProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subcategory",
                table: "ProjectProducts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("UPDATE p SET Subcategory = t.Name FROM ProjectProducts p JOIN ProductTypes t ON t.Id = p.ProductTypeId WHERE p.Subcategory IS NULL;");

            migrationBuilder.AddColumn<int>(
                name: "RejectionReasonId",
                table: "ProductReviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReasonName",
                table: "ProductReviews",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RejectionReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    RequiresComment = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RejectionReasons", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ProductCategories",
                columns: new[] { "Id", "CreatedAtUtc", "Description", "DisplayOrder", "IsActive", "Name", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 10, true, "Hair Care", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 2, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 20, true, "Body Care", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 3, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 30, true, "Face Cream", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "RejectionReasons",
                columns: new[] { "Id", "CreatedAtUtc", "Description", "DisplayOrder", "IsActive", "Name", "RequiresComment", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 10, true, "Price barrier", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 2, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 20, true, "Lack of technology", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 3, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 30, true, "Inability to meet quality requirements", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 4, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 40, true, "Not meeting the NPD", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 5, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 50, true, "Not meeting the MOQ", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 6, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 60, true, "Project with low potential", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 7, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 70, true, "Price too high", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 8, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 80, true, "Formulation quality below expectations", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 9, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 90, true, "Lack of information", false, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 10, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 100, true, "Other", true, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ArchivedAtUtc_CreatedAtUtc",
                table: "Projects",
                columns: new[] { "ArchivedAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProducts_ProductCategoryId",
                table: "ProjectProducts",
                column: "ProductCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProducts_ReviewStatus_CommercialStatus",
                table: "ProjectProducts",
                columns: new[] { "ReviewStatus", "CommercialStatus" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectProducts_Commercial",
                table: "ProjectProducts",
                sql: "[CommercialStatus] IS NULL OR ([ReviewStatus] IN ('Approved','EditedAndApproved') AND [CommercialStatus] IN ('PriceOfferSubmitted','OfferUnderNegotiation','CustomerApprovedOrder','CustomerNotApproved','ImplementationIntoProduction','SalesAndDelivery'))");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReviews_RejectionReasonId",
                table: "ProductReviews",
                column: "RejectionReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_Name",
                table: "ProductCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RejectionReasons_Name",
                table: "RejectionReasons",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductReviews_RejectionReasons_RejectionReasonId",
                table: "ProductReviews",
                column: "RejectionReasonId",
                principalTable: "RejectionReasons",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectProducts_ProductCategories_ProductCategoryId",
                table: "ProjectProducts",
                column: "ProductCategoryId",
                principalTable: "ProductCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM ProjectProducts WHERE ProductTypeId IS NULL OR ProductCategoryId IS NOT NULL OR CommercialStatus IS NOT NULL) OR EXISTS (SELECT 1 FROM ProductReviews WHERE RejectionReasonId IS NOT NULL) THROW 51000, 'Rollback requires an explicit data preservation plan for commercial workflow records.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_ProductReviews_RejectionReasons_RejectionReasonId",
                table: "ProductReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectProducts_ProductCategories_ProductCategoryId",
                table: "ProjectProducts");

            migrationBuilder.DropTable(
                name: "ProductCategories");

            migrationBuilder.DropTable(
                name: "RejectionReasons");

            migrationBuilder.DropIndex(
                name: "IX_Projects_ArchivedAtUtc_CreatedAtUtc",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ProjectProducts_ProductCategoryId",
                table: "ProjectProducts");

            migrationBuilder.DropIndex(
                name: "IX_ProjectProducts_ReviewStatus_CommercialStatus",
                table: "ProjectProducts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectProducts_Commercial",
                table: "ProjectProducts");

            migrationBuilder.DropIndex(
                name: "IX_ProductReviews_RejectionReasonId",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CommercialStatus",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "ProductCategoryId",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "Subcategory",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "RejectionReasonId",
                table: "ProductReviews");

            migrationBuilder.DropColumn(
                name: "RejectionReasonName",
                table: "ProductReviews");

            migrationBuilder.AlterColumn<int>(
                name: "ProductTypeId",
                table: "ProjectProducts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectProducts_RejectionComment",
                table: "ProjectProducts",
                sql: "[ReviewStatus] <> 'Rejected' OR ([ReviewComment] IS NOT NULL AND LEN(LTRIM(RTRIM([ReviewComment]))) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductReviews_RejectionComment",
                table: "ProductReviews",
                sql: "[Decision] <> 'Rejected' OR ([Comment] IS NOT NULL AND LEN(LTRIM(RTRIM([Comment]))) > 0)");
        }
    }
}
