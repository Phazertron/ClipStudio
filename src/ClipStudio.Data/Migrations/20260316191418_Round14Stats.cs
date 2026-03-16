using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round14Stats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlayCount",
                table: "Clips",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayCount",
                table: "Clips");
        }
    }
}
