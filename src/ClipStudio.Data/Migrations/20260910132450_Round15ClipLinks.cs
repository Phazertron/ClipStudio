using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round15ClipLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClipLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceClipId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetClipId = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkType = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClipLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClipLinks_Clips_SourceClipId",
                        column: x => x.SourceClipId,
                        principalTable: "Clips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClipLinks_Clips_TargetClipId",
                        column: x => x.TargetClipId,
                        principalTable: "Clips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClipLinks_SourceClipId_TargetClipId_LinkType",
                table: "ClipLinks",
                columns: new[] { "SourceClipId", "TargetClipId", "LinkType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClipLinks_TargetClipId",
                table: "ClipLinks",
                column: "TargetClipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClipLinks");
        }
    }
}
