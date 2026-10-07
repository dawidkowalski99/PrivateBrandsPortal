using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class FormulaAttachmentsAndManagerOwnedProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs");

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Projects",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresManagerApproval",
                table: "Projects",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "FormulaOptionId",
                table: "ProjectProducts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormulaOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulaOptions", x => x.Id);
                });

            // One-time reference data: later Administration changes are never overwritten on startup.
            migrationBuilder.Sql("""
                INSERT INTO FormulaOptions (Code,Name,IsActive,DisplayOrder,CreatedAtUtc,UpdatedAtUtc)
                VALUES ('NEW_DEVELOPMENT','New development (extended timeline)',1,1,SYSUTCDATETIME(),SYSUTCDATETIME()),
                       ('NEW_FORMULA','New formula (standard timeline)',1,2,SYSUTCDATETIME(),SYSUTCDATETIME()),
                       ('READY_TO_GO','Ready to go',1,3,SYSUTCDATETIME(),SYSUTCDATETIME());
                UPDATE p SET FormulaOptionId=f.Id FROM ProjectProducts p JOIN FormulaOptions f
                ON f.Code=CASE p.FormulaStatus WHEN 'ReadyToGo' THEN 'READY_TO_GO' WHEN 'NewFormula' THEN 'NEW_FORMULA' END;
                """);
            migrationBuilder.CreateTable(
                name: "ProjectAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ProjectProductId = table.Column<int>(type: "int", nullable: true),
                    AttachmentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAttachments", x => x.Id);
                    table.CheckConstraint("CK_ProjectAttachments_Deleted", "([DeletedAtUtc] IS NULL AND [DeletedByUserId] IS NULL) OR ([DeletedAtUtc] IS NOT NULL AND [DeletedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_ProjectAttachments_Product", "[ProjectProductId] IS NULL OR [AttachmentType]='Calculation'");
                    table.CheckConstraint("CK_ProjectAttachments_Size", "[FileSize]>0");
                    table.CheckConstraint("CK_ProjectAttachments_Type", "[AttachmentType] IN ('Brief','Offer','Calculation')");
                    table.ForeignKey(
                        name: "FK_ProjectAttachments_AppUsers_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectAttachments_AppUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectAttachments_ProjectProducts_ProjectProductId",
                        column: x => x.ProjectProductId,
                        principalTable: "ProjectProducts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectAttachments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CreatedByUserId",
                table: "Projects",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProducts_FormulaOptionId",
                table: "ProjectProducts",
                column: "FormulaOptionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs",
                sql: "[ChangeType] IN ('Created','Updated','Deleted','ManagerEdit','ProjectSubmitted','ProjectReassigned','AttachmentUploaded','AttachmentRemoved','ManagerApprovalBypassed')");

            migrationBuilder.CreateIndex(
                name: "IX_FormulaOptions_Code",
                table: "FormulaOptions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttachments_DeletedByUserId",
                table: "ProjectAttachments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttachments_ProjectId_AttachmentType_DeletedAtUtc",
                table: "ProjectAttachments",
                columns: new[] { "ProjectId", "AttachmentType", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttachments_ProjectProductId",
                table: "ProjectAttachments",
                column: "ProjectProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttachments_StorageKey",
                table: "ProjectAttachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAttachments_UploadedByUserId",
                table: "ProjectAttachments",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectProducts_FormulaOptions_FormulaOptionId",
                table: "ProjectProducts",
                column: "FormulaOptionId",
                principalTable: "FormulaOptions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AppUsers_CreatedByUserId",
                table: "Projects",
                column: "CreatedByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectProducts_FormulaOptions_FormulaOptionId",
                table: "ProjectProducts");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AppUsers_CreatedByUserId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "FormulaOptions");

            migrationBuilder.DropTable(
                name: "ProjectAttachments");

            migrationBuilder.DropIndex(
                name: "IX_Projects_CreatedByUserId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ProjectProducts_FormulaOptionId",
                table: "ProjectProducts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RequiresManagerApproval",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "FormulaOptionId",
                table: "ProjectProducts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs",
                sql: "[ChangeType] IN ('Created','Updated','Deleted','ManagerEdit','ProjectSubmitted','ProjectReassigned')");
        }
    }
}
