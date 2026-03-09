using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameIgdbColumnsToGameStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IgdbId",
                table: "Tags",
                newName: "GameStoreAppId");

            migrationBuilder.RenameColumn(
                name: "IgdbCoverUrl",
                table: "Tags",
                newName: "GameCoverUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GameStoreAppId",
                table: "Tags",
                newName: "IgdbId");

            migrationBuilder.RenameColumn(
                name: "GameCoverUrl",
                table: "Tags",
                newName: "IgdbCoverUrl");
        }
    }
}
