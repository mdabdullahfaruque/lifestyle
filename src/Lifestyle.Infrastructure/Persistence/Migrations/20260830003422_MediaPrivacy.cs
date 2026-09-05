using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_private",
                schema: "media",
                table: "media_files",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_private",
                schema: "media",
                table: "media_files");
        }
    }
}
