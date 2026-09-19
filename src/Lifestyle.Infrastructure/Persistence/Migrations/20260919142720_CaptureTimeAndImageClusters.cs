using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaptureTimeAndImageClusters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "captured_at",
                schema: "media",
                table: "media_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cluster_key",
                schema: "catalog",
                table: "import_job_images",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "captured_at",
                schema: "media",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "cluster_key",
                schema: "catalog",
                table: "import_job_images");
        }
    }
}
