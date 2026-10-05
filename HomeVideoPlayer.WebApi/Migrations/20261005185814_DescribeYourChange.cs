using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeVideoPlayer.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class DescribeYourChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupPath",
                table: "Videos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                table: "Videos",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupPath",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "StoragePath",
                table: "Videos");
        }
    }
}
