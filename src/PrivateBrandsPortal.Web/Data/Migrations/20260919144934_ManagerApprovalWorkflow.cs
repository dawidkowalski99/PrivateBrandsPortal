using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManagerApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects",
                sql: "[Status] IN ('Draft','AwaitingManagerReview','PartiallyReviewed','Approved','Rejected','InProgress','Completed','Cancelled','PartiallyApproved')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs",
                sql: "[ChangeType] IN ('Created','Updated','Deleted','ManagerEdit','ProjectSubmitted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_Status",
                table: "Projects",
                sql: "[Status] IN ('Draft','AwaitingManagerReview','PartiallyReviewed','Approved','Rejected','InProgress','Completed','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_ChangeType",
                table: "AuditLogs",
                sql: "[ChangeType] IN ('Created','Updated','Deleted')");
        }
    }
}
