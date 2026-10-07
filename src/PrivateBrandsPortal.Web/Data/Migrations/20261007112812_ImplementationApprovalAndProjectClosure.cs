using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImplementationApprovalAndProjectClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerRejectedAtUtc",
                table: "Projects",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerRejectedByUserId",
                table: "Projects",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerRejectionComment",
                table: "Projects",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerRejectionReasonId",
                table: "Projects",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerRejectionReasonName",
                table: "Projects",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerRejectionReasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerRejectionReasons", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO CustomerRejectionReasons (Code,Name,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc)
                VALUES ('PRICE_TOO_HIGH',N'The price is too high',1,1,'2026-10-07T00:00:00+00:00','2026-10-07T00:00:00+00:00'),
                       ('FORMULATION_NOT_APPROVED',N'Lack of formulation approval',1,2,'2026-10-07T00:00:00+00:00','2026-10-07T00:00:00+00:00'),
                       ('CUSTOMER_CANCELLED',N'Project cancellation by Customer',1,3,'2026-10-07T00:00:00+00:00','2026-10-07T00:00:00+00:00'),
                       ('UNKNOWN',N'Reason unknown',1,4,'2026-10-07T00:00:00+00:00','2026-10-07T00:00:00+00:00');
                """);
            migrationBuilder.CreateTable(
                name: "ImplementationApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectProductId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewerId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImplementationApprovals", x => x.Id);
                    table.CheckConstraint("CK_ImplementationApprovals_Decision", "([Status]='Pending' AND [ReviewerId] IS NULL AND [ReviewedAtUtc] IS NULL) OR ([Status]<>'Pending' AND [ReviewerId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ImplementationApprovals_Status", "[Status] IN ('Pending','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_ImplementationApprovals_AppUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImplementationApprovals_AppUsers_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImplementationApprovals_ProjectProducts_ProjectProductId",
                        column: x => x.ProjectProductId,
                        principalTable: "ProjectProducts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CustomerRejectedByUserId",
                table: "Projects",
                column: "CustomerRejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CustomerRejectionReasonId",
                table: "Projects",
                column: "CustomerRejectionReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRejectionReasons_Code",
                table: "CustomerRejectionReasons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImplementationApprovals_ProjectProductId",
                table: "ImplementationApprovals",
                column: "ProjectProductId",
                unique: true,
                filter: "[Status]='Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ImplementationApprovals_ProjectProductId_Id",
                table: "ImplementationApprovals",
                columns: new[] { "ProjectProductId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ImplementationApprovals_RequestedByUserId",
                table: "ImplementationApprovals",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImplementationApprovals_ReviewerId",
                table: "ImplementationApprovals",
                column: "ReviewerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AppUsers_CustomerRejectedByUserId",
                table: "Projects",
                column: "CustomerRejectedByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_CustomerRejectionReasons_CustomerRejectionReasonId",
                table: "Projects",
                column: "CustomerRejectionReasonId",
                principalTable: "CustomerRejectionReasons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AppUsers_CustomerRejectedByUserId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_CustomerRejectionReasons_CustomerRejectionReasonId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "CustomerRejectionReasons");

            migrationBuilder.DropTable(
                name: "ImplementationApprovals");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CustomerRejectedByUserId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CustomerRejectionReasonId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CustomerRejectedAtUtc",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CustomerRejectedByUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CustomerRejectionComment",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CustomerRejectionReasonId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "CustomerRejectionReasonName",
                table: "Projects");
        }
    }
}
