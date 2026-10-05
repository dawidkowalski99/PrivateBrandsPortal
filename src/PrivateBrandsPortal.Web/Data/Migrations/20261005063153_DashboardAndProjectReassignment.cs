using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DashboardAndProjectReassignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "AuditLogs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { 5, "REASSIGN_PROJECTS", "Allows changing the Project Manager assigned to a project.", 50, true, "Reassign projects" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs",
                sql: "[ChangeType] IN ('Created','Updated','Deleted','ManagerEdit','ProjectSubmitted','ProjectReassigned')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM AuditLogs WHERE ChangeType = 'ProjectReassigned' OR Reason IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM AppUserPermissions WHERE PermissionId = 5)
                    THROW 51000, 'Cannot remove project reassignment while its history or permission grants exist.', 1;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs");

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "AuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs",
                sql: "[ChangeType] IN ('Created','Updated','Deleted','ManagerEdit','ProjectSubmitted')");
        }
    }
}
