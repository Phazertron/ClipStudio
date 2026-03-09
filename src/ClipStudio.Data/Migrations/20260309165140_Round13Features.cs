using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round13Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Highlights",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Highlights",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GameTagAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false),
                    AliasString = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameTagAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameTagAliases_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameTagAliases_AliasString",
                table: "GameTagAliases",
                column: "AliasString",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameTagAliases_TagId",
                table: "GameTagAliases",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameTagAliases");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Highlights");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Highlights");
        }
    }
}
