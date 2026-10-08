using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrivateBrandsPortal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProductCustomerRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerRejectedAtUtc",
                table: "ProjectProducts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerRejectedByUserId",
                table: "ProjectProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerRejectionComment",
                table: "ProjectProducts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerRejectionReasonId",
                table: "ProjectProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerRejectionReasonName",
                table: "ProjectProducts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProducts_CustomerRejectedByUserId",
                table: "ProjectProducts",
                column: "CustomerRejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectProducts_CustomerRejectionReasonId",
                table: "ProjectProducts",
                column: "CustomerRejectionReasonId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectProducts_AppUsers_CustomerRejectedByUserId",
                table: "ProjectProducts",
                column: "CustomerRejectedByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectProducts_CustomerRejectionReasons_CustomerRejectionReasonId",
                table: "ProjectProducts",
                column: "CustomerRejectionReasonId",
                principalTable: "CustomerRejectionReasons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectProducts_AppUsers_CustomerRejectedByUserId",
                table: "ProjectProducts");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectProducts_CustomerRejectionReasons_CustomerRejectionReasonId",
                table: "ProjectProducts");

            migrationBuilder.DropIndex(
                name: "IX_ProjectProducts_CustomerRejectedByUserId",
                table: "ProjectProducts");

            migrationBuilder.DropIndex(
                name: "IX_ProjectProducts_CustomerRejectionReasonId",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "CustomerRejectedAtUtc",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "CustomerRejectedByUserId",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "CustomerRejectionComment",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "CustomerRejectionReasonId",
                table: "ProjectProducts");

            migrationBuilder.DropColumn(
                name: "CustomerRejectionReasonName",
                table: "ProjectProducts");
        }
    }
}
