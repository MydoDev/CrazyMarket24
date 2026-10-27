using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrazyMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2VendorAndLoyalty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "StoreSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "StoreSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActionPrice",
                table: "Products",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActionValidUntil",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BonusPointsReward",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPoints",
                table: "Customers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Ico = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    TotpSecret = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AllowDynamicPricing = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PohodaSynced = table.Column<bool>(type: "INTEGER", nullable: false),
                    PohodaSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDeliveries_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockDeliveryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockDeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDeliveryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDeliveryItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDeliveryItems_StockDeliveries_StockDeliveryId",
                        column: x => x.StockDeliveryId,
                        principalTable: "StockDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreSessions_SupplierId",
                table: "StoreSessions",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SupplierId",
                table: "Products",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDeliveries_PohodaSynced_DeliveredAt",
                table: "StockDeliveries",
                columns: new[] { "PohodaSynced", "DeliveredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDeliveries_SupplierId",
                table: "StockDeliveries",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDeliveryItems_ProductId",
                table: "StockDeliveryItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDeliveryItems_StockDeliveryId",
                table: "StockDeliveryItems",
                column: "StockDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Ico",
                table: "Suppliers",
                column: "Ico",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Suppliers_SupplierId",
                table: "Products",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StoreSessions_Suppliers_SupplierId",
                table: "StoreSessions",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Suppliers_SupplierId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_StoreSessions_Suppliers_SupplierId",
                table: "StoreSessions");

            migrationBuilder.DropTable(
                name: "StockDeliveryItems");

            migrationBuilder.DropTable(
                name: "StockDeliveries");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_StoreSessions_SupplierId",
                table: "StoreSessions");

            migrationBuilder.DropIndex(
                name: "IX_Products_SupplierId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "StoreSessions");

            migrationBuilder.DropColumn(
                name: "ActionPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ActionValidUntil",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BonusPointsReward",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LoyaltyPoints",
                table: "Customers");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "StoreSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
