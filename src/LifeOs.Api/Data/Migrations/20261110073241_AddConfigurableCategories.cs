using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LifeOs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurableCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiSettingsVersion",
                table: "Captures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Captures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "Captures",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveVersion = table.Column<int>(type: "integer", nullable: false),
                    DraftJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActiveJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Captures_CategoryId",
                table: "Captures",
                column: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSettings");

            migrationBuilder.DropIndex(
                name: "IX_Captures_CategoryId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "AiSettingsVersion",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Captures");

            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "Captures");
        }
    }
}
