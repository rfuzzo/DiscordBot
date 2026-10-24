using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEtaReleaseDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseAtUtc",
                table: "EtaCounters",
                type: "TEXT",
                nullable: true);

            // Counters from before this column existed: today plus the delay, like the last announcement.
            migrationBuilder.Sql(
                "UPDATE EtaCounters SET ReleaseAtUtc = datetime('now', '+' || Count || ' days') WHERE ReleaseAtUtc IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleaseAtUtc",
                table: "EtaCounters");
        }
    }
}
