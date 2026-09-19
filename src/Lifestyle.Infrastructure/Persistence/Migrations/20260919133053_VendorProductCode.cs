using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VendorProductCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "vendor_product_code",
                schema: "catalog",
                table: "products",
                type: "citext",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_vendor_id_vendor_product_code",
                schema: "catalog",
                table: "products",
                columns: new[] { "vendor_id", "vendor_product_code" },
                unique: true,
                filter: "vendor_product_code IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_vendor_id_vendor_product_code",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "vendor_product_code",
                schema: "catalog",
                table: "products");
        }
    }
}
