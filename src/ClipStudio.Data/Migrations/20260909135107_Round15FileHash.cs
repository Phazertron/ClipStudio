using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round15FileHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "Clips",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clips_FileHash",
                table: "Clips",
                column: "FileHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clips_FileHash",
                table: "Clips");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "Clips");
        }
    }
}
