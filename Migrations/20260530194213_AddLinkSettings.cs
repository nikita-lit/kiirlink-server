using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KiirlinkServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Links",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Links",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Links");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Links");
        }
    }
}
