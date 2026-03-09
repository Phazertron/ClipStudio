using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClipStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round10Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThumbnailPath",
                table: "Highlights",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EndTime",
                table: "ExportJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StartTime",
                table: "ExportJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedAt",
                table: "Clips",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Clips",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TrashPath",
                table: "Clips",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clips_IsDeleted",
                table: "Clips",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clips_IsDeleted",
                table: "Clips");

            migrationBuilder.DropColumn(
                name: "ThumbnailPath",
                table: "Highlights");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Clips");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Clips");

            migrationBuilder.DropColumn(
                name: "TrashPath",
                table: "Clips");
        }
    }
}
