using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class TranscriptionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transcriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClipId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SrtFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transcriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transcriptions_Clips_ClipId",
                        column: x => x.ClipId,
                        principalTable: "Clips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TranscriptionSegments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TranscriptionId = table.Column<int>(type: "INTEGER", nullable: false),
                    IndexNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMs = table.Column<long>(type: "INTEGER", nullable: false),
                    EndMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptionSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranscriptionSegments_Transcriptions_TranscriptionId",
                        column: x => x.TranscriptionId,
                        principalTable: "Transcriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transcriptions_ClipId",
                table: "Transcriptions",
                column: "ClipId");

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptionSegments_TranscriptionId",
                table: "TranscriptionSegments",
                column: "TranscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranscriptionSegments");

            migrationBuilder.DropTable(
                name: "Transcriptions");
        }
    }
}
