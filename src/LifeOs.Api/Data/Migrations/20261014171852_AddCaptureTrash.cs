using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Captures",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Captures");
        }
    }
}
