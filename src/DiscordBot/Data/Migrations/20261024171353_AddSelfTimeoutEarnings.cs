using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfTimeoutEarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SelfTimeoutDayUtc",
                table: "Wallets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SelfTimeoutEarned",
                table: "Wallets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelfTimeoutDayUtc",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "SelfTimeoutEarned",
                table: "Wallets");
        }
    }
}
