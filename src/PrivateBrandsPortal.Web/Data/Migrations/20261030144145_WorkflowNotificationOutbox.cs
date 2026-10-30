using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NotificationApproverId",
                table: "AppUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    TargetUserId = table.Column<int>(type: "int", nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_NotificationApproverId",
                table: "AppUsers",
                column: "NotificationApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_EventKey",
                table: "NotificationOutbox",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status_NextAttemptAtUtc_Id",
                table: "NotificationOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_AppUsers_NotificationApproverId",
                table: "AppUsers",
                column: "NotificationApproverId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_AppUsers_NotificationApproverId",
                table: "AppUsers");

            migrationBuilder.DropTable(
                name: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_NotificationApproverId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "NotificationApproverId",
                table: "AppUsers");
        }
    }
}
