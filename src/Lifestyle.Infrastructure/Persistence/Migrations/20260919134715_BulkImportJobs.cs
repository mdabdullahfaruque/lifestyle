using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BulkImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_jobs",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_count = table.Column<int>(type: "integer", nullable: false),
                    updated_count = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_job_images",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    product_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    matched_by = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_job_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_job_images_import_jobs_import_job_id",
                        column: x => x.import_job_id,
                        principalSchema: "catalog",
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_job_rows",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    target_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    affects_live_product = table.Column<bool>(type: "boolean", nullable: false),
                    live_update_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    is_skipped = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_job_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_job_rows_import_jobs_import_job_id",
                        column: x => x.import_job_id,
                        principalSchema: "catalog",
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_job_images_import_job_id_product_code",
                schema: "catalog",
                table: "import_job_images",
                columns: new[] { "import_job_id", "product_code" });

            migrationBuilder.CreateIndex(
                name: "ix_import_job_rows_import_job_id_product_code",
                schema: "catalog",
                table: "import_job_rows",
                columns: new[] { "import_job_id", "product_code" });

            migrationBuilder.CreateIndex(
                name: "ix_import_job_rows_import_job_id_row_number",
                schema: "catalog",
                table: "import_job_rows",
                columns: new[] { "import_job_id", "row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_status_expires_at",
                schema: "catalog",
                table: "import_jobs",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_vendor_id_created_at",
                schema: "catalog",
                table: "import_jobs",
                columns: new[] { "vendor_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_job_images",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "import_job_rows",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "import_jobs",
                schema: "catalog");
        }
    }
}
