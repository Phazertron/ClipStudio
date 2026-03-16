using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round14BrokenClips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBroken",
                table: "Clips",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Clips_IsBroken",
                table: "Clips",
                column: "IsBroken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clips_IsBroken",
                table: "Clips");

            migrationBuilder.DropColumn(
                name: "IsBroken",
                table: "Clips");
        }
    }
}
