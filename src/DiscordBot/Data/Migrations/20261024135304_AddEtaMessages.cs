using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEtaMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EtaMessages",
                columns: table => new
                {
                    MessageId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtaMessages", x => x.MessageId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EtaMessages");
        }
    }
}
